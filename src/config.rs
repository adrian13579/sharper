use anyhow::{Context, Result, bail, ensure};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::{BTreeMap, HashSet},
    path::{Path, PathBuf},
};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Server {
    #[serde(default = "default_id")]
    pub id: String,
    pub command: String,
    #[serde(default)]
    pub arguments: Vec<String>,
    #[serde(deserialize_with = "unique_languages")]
    pub languages: BTreeMap<String, String>,
    #[serde(default)]
    pub initialization_options: Value,
    #[serde(default)]
    pub settings: Value,
    #[serde(default)]
    pub ready_notification: Option<String>,
    #[serde(default)]
    pub diagnostics: DiagnosticMode,
    #[serde(default)]
    pub require_result_id: bool,
    #[serde(default = "yes")]
    pub navigation: bool,
}
fn default_id() -> String {
    "custom".into()
}
fn yes() -> bool {
    true
}
#[derive(Clone, Copy, Debug, Default, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub enum DiagnosticMode {
    #[default]
    Pull,
    TypeScript,
}
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
pub struct Config {
    pub servers: Vec<Server>,
}
impl Config {
    pub fn load(path: &Path) -> Result<Self> {
        let bytes = std::fs::read(path)
            .with_context(|| format!("Cannot read LSP configuration {}", path.display()))?;
        let base = std::fs::canonicalize(path)?
            .parent()
            .context("Configuration has no parent")?
            .to_path_buf();
        Self::parse(&bytes, &base)
    }
    pub(crate) fn parse(bytes: &[u8], base: &Path) -> Result<Self> {
        ensure!(
            bytes.len() <= 1024 * 1024,
            "LSP configuration exceeds 1 MiB"
        );
        // Deserialize structs directly so duplicate fields fail instead of silently winning.
        let value: Value = serde_json::from_slice(bytes)?;
        let mut config: Self = if value.get("servers").is_some() {
            serde_json::from_slice(bytes)?
        } else {
            Self {
                servers: vec![serde_json::from_slice(bytes)?],
            }
        };
        ensure!(
            !config.servers.is_empty() && config.servers.len() <= 32,
            "Configure between 1 and 32 LSP servers"
        );
        let mut ids = HashSet::new();
        for server in &mut config.servers {
            ensure!(
                !server.id.is_empty()
                    && server.id.len() <= 64
                    && server
                        .id
                        .chars()
                        .all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_'),
                "Invalid server id"
            );
            ensure!(
                ids.insert(server.id.clone()),
                "Duplicate server id {}",
                server.id
            );
            ensure!(
                !server.command.trim().is_empty() && !server.command.contains('\0'),
                "Server command is required"
            );
            let lower = server.command.to_lowercase();
            ensure!(
                !lower.ends_with(".cmd") && !lower.ends_with(".bat"),
                "Use an executable or explicit runtime instead of a .cmd/.bat shell launcher"
            );
            if server.command.contains('/') || server.command.contains('\\') {
                server.command = base.join(&server.command).to_string_lossy().into_owned();
            }
            ensure!(
                !server.arguments.iter().any(|a| a.contains('\0')),
                "Arguments cannot contain NUL"
            );
            ensure!(
                !server.languages.is_empty(),
                "Server {} requires extension-to-language-ID mappings",
                server.id
            );
            let mut normalized = BTreeMap::new();
            for (ext, lang) in &server.languages {
                ensure!(
                    ext.starts_with('.')
                        && ext.len() > 1
                        && ext[1..]
                            .chars()
                            .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-'),
                    "Invalid extension {ext}"
                );
                ensure!(!lang.trim().is_empty(), "Language ID cannot be empty");
                ensure!(
                    normalized
                        .insert(ext.to_lowercase(), lang.clone())
                        .is_none(),
                    "Duplicate extension {ext}"
                );
            }
            server.languages = normalized;
            ensure!(
                !server.require_result_id || server.diagnostics == DiagnosticMode::Pull,
                "requireResultId applies only to document pulls"
            );
            for value in [&server.settings, &server.initialization_options] {
                ensure!(
                    value.is_null() || value.is_object(),
                    "settings and initializationOptions must be objects"
                );
            }
            if let Some(method) = &server.ready_notification
                && method.trim().is_empty()
            {
                bail!("readyNotification cannot be empty");
            }
        }
        Ok(config)
    }
}
impl Server {
    pub fn language(&self, path: &Path) -> Option<&str> {
        self.languages
            .get(&format!(".{}", path.extension()?.to_str()?.to_lowercase()))
            .map(String::as_str)
    }
    pub fn supports(&self, path: &Path) -> bool {
        self.language(path).is_some()
    }
    pub fn section(&self, section: Option<&str>) -> Value {
        match section {
            None | Some("") => self.settings.clone(),
            Some(key) => key
                .split('.')
                .fold(&self.settings, |v, k| v.get(k).unwrap_or(&Value::Null))
                .clone(),
        }
    }
}
pub fn absolute(path: &Path) -> Result<PathBuf> {
    Ok(std::path::absolute(path)?)
}

fn unique_languages<'de, D: serde::Deserializer<'de>>(
    deserializer: D,
) -> std::result::Result<BTreeMap<String, String>, D::Error> {
    struct Unique;
    impl<'de> serde::de::Visitor<'de> for Unique {
        type Value = BTreeMap<String, String>;
        fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
            f.write_str("unique extension-to-language mappings")
        }
        fn visit_map<A: serde::de::MapAccess<'de>>(
            self,
            mut entries: A,
        ) -> std::result::Result<Self::Value, A::Error> {
            let mut result = BTreeMap::new();
            while let Some((key, value)) = entries.next_entry::<String, String>()? {
                if result.insert(key.clone(), value).is_some() {
                    return Err(serde::de::Error::custom(format!(
                        "Duplicate extension {key}"
                    )));
                }
            }
            Ok(result)
        }
    }
    deserializer.deserialize_map(Unique)
}
