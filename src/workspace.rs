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
            .find(|d| d.join(".git").exists())
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
                    .map(|e| {
                        e.paths
                            .iter()
                            .any(|p| !ignored(p.strip_prefix(&base).unwrap_or(p)))
                    })
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
                    queue.push(path);
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
