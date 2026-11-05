use crate::config::Server;
use anyhow::{Context, Result, ensure};
use notify::{RecommendedWatcher, RecursiveMode, Watcher};
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
};
use tokio::io::AsyncReadExt;
pub const IGNORED: &[&str] = &[".git", "node_modules", "target", "artifacts", ".next"];
#[derive(Clone, Debug, PartialEq)]
pub struct Input {
    pub hash: [u8; 32],
    pub text: Option<String>,
}
#[derive(Clone, Debug, PartialEq)]
pub struct Snapshot(pub BTreeMap<PathBuf, Input>);
pub struct Boundary {
    pub root: PathBuf,
    pub epoch: Arc<AtomicU64>,
    _watcher: RecommendedWatcher,
}
fn ignored(path: &Path) -> bool {
    path.components()
        .any(|c| IGNORED.iter().any(|name| c.as_os_str() == *name))
}
fn checkout(path: &Path) -> bool {
    std::fs::symlink_metadata(path.join(".git"))
        .is_ok_and(|metadata| metadata.is_file() || metadata.is_dir())
}
fn nested_checkout<'a>(root: &Path, path: &'a Path) -> Option<&'a Path> {
    path.ancestors()
        .take_while(|ancestor| *ancestor != root && contained(root, ancestor))
        .find(|ancestor| checkout(ancestor))
}
fn invalidates(event: &notify::Event, root: &Path) -> bool {
    use notify::event::{AccessKind, AccessMode, EventKind};
    if event.need_rescan() {
        return true;
    }
    // Linux reports opens and reads made by our own snapshots and language services.
    if matches!(
        event.kind,
        EventKind::Access(
            AccessKind::Read | AccessKind::Open(_) | AccessKind::Close(AccessMode::Read)
        )
    ) {
        return false;
    }
    event.paths.is_empty()
        || event.paths.iter().any(|path| {
            let Ok(relative) = path.strip_prefix(root) else {
                return true;
            };
            // A new or removed marker changes which checkout owns these inputs.
            // Keep this observable even after a removed marker is no longer on disk.
            if path.file_name().is_some_and(|name| name == ".git") {
                let parent = path.parent().unwrap_or(root);
                return parent != root && !ignored(parent.strip_prefix(root).unwrap_or(parent));
            }
            if ignored(relative) {
                return false;
            }
            // The checkout directory itself can be added, removed or renamed.
            // Its contents belong to a separate workspace and session.
            nested_checkout(root, path).is_none_or(|checkout| checkout == path)
        })
}
pub fn contained(root: &Path, path: &Path) -> bool {
    #[cfg(windows)]
    {
        let root = root.to_string_lossy().to_lowercase();
        let path = path.to_string_lossy().to_lowercase();
        path == root || path.starts_with(&(root.trim_end_matches(['\\', '/']).to_owned() + "\\"))
    }
    #[cfg(not(windows))]
    {
        path.starts_with(root)
    }
}
pub fn clean(path: PathBuf) -> PathBuf {
    #[cfg(windows)]
    {
        let s = path.to_string_lossy();
        if let Some(rest) = s.strip_prefix(r"\\?\UNC\") {
            return PathBuf::from(format!(r"\\{rest}"));
        }
        if let Some(rest) = s.strip_prefix("\\\\?\\") {
            return PathBuf::from(rest);
        }
    }
    path
}
pub fn resolve(
    workspace: Option<&str>,
    path: Option<&str>,
    fallback: Option<&Path>,
) -> Result<(PathBuf, PathBuf)> {
    let root = if let Some(w) = workspace {
        PathBuf::from(w)
    } else if let Some(p) = path.filter(|p| Path::new(p).is_absolute()) {
        let input = Path::new(p);
        let start = if input.is_dir() {
            input
        } else {
            input.parent().context("No parent directory")?
        };
        start
            .ancestors()
            .find(|d| checkout(d))
            .unwrap_or(start)
            .to_path_buf()
    } else {
        fallback
            .context(
                "Pass workspace as an absolute repository directory or an absolute source path",
            )?
            .to_path_buf()
    };
    ensure!(root.is_absolute(), "workspace must be absolute");
    ensure!(
        !std::fs::symlink_metadata(&root)?.file_type().is_symlink(),
        "Symlink workspaces are unsupported"
    );
    let root = clean(std::fs::canonicalize(root)?);
    ensure!(
        root.to_str().is_some(),
        "Non-UTF-8 workspace paths are unsupported"
    );
    ensure!(root.is_dir(), "Workspace must be a directory");
    let requested = path.map(|p| root.join(p)).unwrap_or_else(|| root.clone());
    // Resolve dot components before containment, but never follow symlinks silently.
    let mut normalized = PathBuf::new();
    for c in requested.components() {
        match c {
            std::path::Component::ParentDir => {
                normalized.pop();
            }
            std::path::Component::CurDir => {}
            _ => normalized.push(c.as_os_str()),
        }
    }
    ensure!(
        contained(&root, &normalized),
        "Requested path must be inside workspace"
    );
    let mut current = root.clone();
    for part in normalized
        .strip_prefix(&root)
        .unwrap_or(Path::new(""))
        .components()
    {
        current.push(part);
        ensure!(
            !std::fs::symlink_metadata(&current)?
                .file_type()
                .is_symlink(),
            "Symlinks are outside the verification boundary"
        );
    }
    let requested = clean(std::fs::canonicalize(normalized)?);
    ensure!(
        contained(&root, &requested),
        "Requested path escapes workspace"
    );
    let start = if requested.is_dir() {
        requested.as_path()
    } else {
        requested.parent().context("No parent directory")?
    };
    let root = nested_checkout(&root, start)
        .map(Path::to_path_buf)
        .unwrap_or(root);
    Ok((root, requested))
}
impl Boundary {
    pub fn new(root: PathBuf) -> Result<Self> {
        let epoch = Arc::new(AtomicU64::new(0));
        let counter = epoch.clone();
        let base = root.clone();
        let mut watcher =
            notify::recommended_watcher(move |event: notify::Result<notify::Event>| {
                if event
                    .as_ref()
                    .map(|e| invalidates(e, &base))
                    .unwrap_or(true)
                {
                    counter.fetch_add(1, Ordering::SeqCst);
                }
            })?;
        watcher.watch(&root, RecursiveMode::Recursive)?;
        Ok(Self {
            root,
            epoch,
            _watcher: watcher,
        })
    }
    pub fn revision(&self) -> u64 {
        self.epoch.load(Ordering::SeqCst)
    }
    pub async fn snapshot(&self, server: &Server) -> Result<Snapshot> {
        let mut queue = vec![self.root.clone()];
        let mut files = BTreeMap::new();
        let mut total = 0usize;
        while let Some(dir) = queue.pop() {
            let mut entries = tokio::fs::read_dir(&dir).await?;
            while let Some(entry) = entries.next_entry().await? {
                let path = entry.path();
                ensure!(
                    path.to_str().is_some(),
                    "Non-UTF-8 filesystem paths are unsupported"
                );
                if ignored(path.strip_prefix(&self.root)?) {
                    continue;
                }
                let meta = tokio::fs::symlink_metadata(&path).await?;
                ensure!(
                    !meta.file_type().is_symlink(),
                    "Cannot verify symlink {}",
                    path.display()
                );
                if meta.is_dir() {
                    if !checkout(&path) {
                        queue.push(path);
                    }
                    continue;
                }
                ensure!(
                    meta.is_file(),
                    "Unsupported workspace entry {}",
                    path.display()
                );
                ensure!(
                    meta.len() <= 32 * 1024 * 1024,
                    "Workspace file exceeds 32 MiB: {}",
                    path.display()
                );
                let file = tokio::fs::File::open(&path).await?;
                let mut bytes = Vec::new();
                file.take(32 * 1024 * 1024 + 1)
                    .read_to_end(&mut bytes)
                    .await?;
                total += bytes.len();
                ensure!(
                    bytes.len() <= 32 * 1024 * 1024
                        && total <= 512 * 1024 * 1024
                        && files.len() < 100_000,
                    "Workspace reconciliation limit exceeded"
                );
                let text = if server.supports(&path) {
                    let text = std::str::from_utf8(&bytes).context("Source text must be UTF-8")?;
                    Some(text.trim_start_matches('\u{feff}').to_owned())
                } else {
                    None
                };
                files.insert(
                    path,
                    Input {
                        hash: Sha256::digest(&bytes).into(),
                        text,
                    },
                );
            }
        }
        Ok(Snapshot(files))
    }
}
impl Snapshot {
    pub fn reloads(&self, previous: &Self, server: &Server) -> bool {
        self.0.keys().ne(previous.0.keys())
            || self.0.iter().any(|(p, f)| {
                !server.supports(p) && previous.0.get(p).is_none_or(|old| old.hash != f.hash)
            })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use notify::{
        Event,
        event::{AccessKind, AccessMode, CreateKind, EventKind, Flag, ModifyKind, RemoveKind},
    };
    fn workspace() -> (tempfile::TempDir, PathBuf) {
        let temporary = tempfile::tempdir().unwrap();
        let root = temporary.path().join("main");
        std::fs::create_dir(&root).unwrap();
        std::fs::create_dir(root.join(".git")).unwrap();
        let root = clean(std::fs::canonicalize(root).unwrap());
        (temporary, root)
    }
    fn worktree(root: &Path) -> PathBuf {
        let tree = root.join("agents/worker");
        std::fs::create_dir_all(tree.join("src")).unwrap();
        // Worktree markers point outside the checkout; never use that directory as its root.
        std::fs::write(
            tree.join(".git"),
            "gitdir: /shared/repository/worktrees/worker\n",
        )
        .unwrap();
        std::fs::write(tree.join("src/example.orbit"), "violation").unwrap();
        tree
    }
    #[test]
    fn worktree_paths_select_the_checkout_for_explicit_inferred_and_fallback_roots() {
        let (_temporary, root) = workspace();
        let tree = worktree(&root);
        let source = tree.join("src/example.orbit");
        let root_text = root.to_str().unwrap();
        let source_text = source.to_str().unwrap();
        for resolved in [
            resolve(None, Some(source_text), None),
            resolve(Some(root_text), Some(source_text), None),
            resolve(
                Some(root_text),
                Some("agents/worker/src/example.orbit"),
                None,
            ),
            resolve(None, Some("agents/worker/src/example.orbit"), Some(&root)),
            resolve(
                Some(tree.to_str().unwrap()),
                Some("src/example.orbit"),
                None,
            ),
        ] {
            assert_eq!(resolved.unwrap(), (tree.clone(), source.clone()));
        }
        assert_eq!(
            resolve(Some(root_text), Some("agents/worker"), None).unwrap(),
            (tree.clone(), tree.clone())
        );
        assert_eq!(
            resolve(Some(tree.to_str().unwrap()), None, None).unwrap(),
            (tree.clone(), tree)
        );
    }
    #[test]
    fn nested_repository_directories_also_select_their_own_root() {
        let (_temporary, root) = workspace();
        let tree = root.join("nested");
        std::fs::create_dir_all(tree.join(".git")).unwrap();
        std::fs::write(tree.join("example.orbit"), "fixed").unwrap();
        assert_eq!(
            resolve(
                Some(root.to_str().unwrap()),
                Some("nested/example.orbit"),
                None
            )
            .unwrap(),
            (tree.clone(), tree.join("example.orbit"))
        );
    }
    #[test]
    fn worktree_resolution_preserves_explicit_workspace_containment() {
        let (temporary, root) = workspace();
        let tree = worktree(temporary.path());
        let source = tree.join("src/example.orbit");
        for path in [
            source.to_str().unwrap(),
            "../agents/worker/src/example.orbit",
        ] {
            let error = resolve(Some(root.to_str().unwrap()), Some(path), None).unwrap_err();
            assert!(error.to_string().contains("inside workspace"), "{error}");
        }
    }
    #[tokio::test]
    async fn snapshots_keep_nested_checkouts_out_of_the_parent_workspace() {
        let (_temporary, root) = workspace();
        let tree = worktree(&root);
        let nested = root.join("nested");
        std::fs::create_dir_all(nested.join(".git")).unwrap();
        std::fs::write(nested.join("example.orbit"), "violation").unwrap();
        std::fs::write(root.join("example.orbit"), "fixed").unwrap();
        let server: Server = serde_json::from_value(serde_json::json!({
            "command":"unused", "languages":{".orbit":"planet"}
        }))
        .unwrap();
        let parent = Boundary::new(root.clone()).unwrap();
        let snapshot = parent.snapshot(&server).await.unwrap();
        assert_eq!(snapshot.0.len(), 1);
        assert_eq!(
            snapshot.0[&root.join("example.orbit")].text.as_deref(),
            Some("fixed")
        );
        let worker = Boundary::new(tree.clone()).unwrap();
        let snapshot = worker.snapshot(&server).await.unwrap();
        assert_eq!(snapshot.0.len(), 1);
        assert_eq!(
            snapshot.0[&tree.join("src/example.orbit")].text.as_deref(),
            Some("violation")
        );
    }
    #[test]
    fn nested_checkout_events_preserve_parent_freshness_but_topology_changes_invalidate() {
        let (_temporary, root) = workspace();
        let tree = worktree(&root);
        let changed = Event::new(EventKind::Modify(ModifyKind::Any));
        assert!(!invalidates(
            &changed.clone().add_path(tree.join("src/example.orbit")),
            &root
        ));
        assert!(invalidates(
            &changed.clone().add_path(tree.join("src/example.orbit")),
            &tree
        ));
        assert!(invalidates(&changed.clone().add_path(tree.clone()), &root));
        assert!(!invalidates(
            &changed.add_path(root.join(".git/index")),
            &root
        ));
        assert!(invalidates(
            &Event::new(EventKind::Create(CreateKind::File)).add_path(tree.join(".git")),
            &root
        ));
        std::fs::remove_file(tree.join(".git")).unwrap();
        assert!(invalidates(
            &Event::new(EventKind::Remove(RemoveKind::File)).add_path(tree.join(".git")),
            &root
        ));
        std::fs::remove_dir_all(&tree).unwrap();
        assert!(invalidates(
            &Event::new(EventKind::Remove(RemoveKind::Folder)).add_path(tree),
            &root
        ));
    }
    #[test]
    fn reading_inputs_preserves_freshness() {
        let root = Path::new("workspace");
        for kind in [
            AccessKind::Read,
            AccessKind::Open(AccessMode::Read),
            AccessKind::Open(AccessMode::Any),
            AccessKind::Close(AccessMode::Read),
        ] {
            let event = Event::new(EventKind::Access(kind)).add_path(root.join("source.orbit"));
            assert!(!invalidates(&event, root));
        }
    }
    #[test]
    fn writes_and_uncertain_watcher_events_invalidate_inputs() {
        let root = Path::new("workspace");
        for kind in [
            EventKind::Access(AccessKind::Close(AccessMode::Write)),
            EventKind::Modify(ModifyKind::Any),
            EventKind::Create(CreateKind::Any),
            EventKind::Remove(RemoveKind::Any),
            EventKind::Any,
        ] {
            assert!(invalidates(
                &Event::new(kind).add_path(root.join("source.orbit")),
                root
            ));
        }
        assert!(invalidates(&Event::new(EventKind::Any), root));
        assert!(invalidates(
            &Event::new(EventKind::Access(AccessKind::Read)).set_flag(Flag::Rescan),
            root
        ));
    }
    #[test]
    fn excluded_dependency_events_preserve_freshness() {
        let root = Path::new("workspace");
        let event = Event::new(EventKind::Modify(ModifyKind::Any))
            .add_path(root.join("node_modules/dependency.js"));
        assert!(!invalidates(&event, root));
    }
}
