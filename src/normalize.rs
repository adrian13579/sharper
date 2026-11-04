use anyhow::{Context, Result, ensure};
use serde_json::{Value, json};
use std::path::{Path, PathBuf};
use url::Url;
pub fn uri(path: &Path) -> Result<String> {
    Ok(Url::from_file_path(path)
        .map_err(|_| anyhow::anyhow!("Invalid file path {}", path.display()))?
        .to_string())
}
pub fn file(uri: &str) -> Option<PathBuf> {
    Url::parse(uri)
        .ok()?
        .to_file_path()
        .ok()
        .map(crate::workspace::clean)
        .filter(|path| path.to_str().is_some())
}
pub fn position(value: &Value) -> Result<Value> {
    let line = value["line"].as_u64().context("Invalid line")?;
    let character = value["character"]
        .as_u64()
        .context("Invalid UTF-16 character")?;
    ensure!(
        line <= i32::MAX as u64 && character <= i32::MAX as u64,
        "Position overflow"
    );
    Ok(json!({"line":line,"character":character}))
}
pub fn range(value: &Value) -> Result<Value> {
    let start = position(&value["start"])?;
    let end = position(&value["end"])?;
    ensure!(
        (start["line"].as_u64(), start["character"].as_u64())
            <= (end["line"].as_u64(), end["character"].as_u64()),
        "Reversed range"
    );
    Ok(json!({"start":start,"end":end}))
}
pub fn diagnostics(items: &Value, document: &str) -> Result<Vec<Value>> {
    items.as_array().context("Diagnostic items must be an array")?.iter().map(|item|{
        let message=item["message"].as_str().context("Diagnostic message must be a string")?;
        let r=range(&item["range"])?;
        let code=match &item["code"] {Value::String(v)=>v.clone(),Value::Number(v)=>v.to_string(),Value::Null=>String::new(),_=>anyhow::bail!("Invalid diagnostic code")};
        let severity=match item["severity"].as_i64(){Some(1)=>"error".into(),Some(2)=>"warning".into(),Some(3)=>"information".into(),Some(4)=>"hint".into(),Some(v)=>v.to_string(),None if item["severity"].is_null()=>"unknown".into(),_=>anyhow::bail!("Invalid severity")};
        let mut d=json!({"code":code,"message":message,"severity":severity,"filePath":file(document),"startLine":r["start"]["line"],"startColumn":r["start"]["character"],"endLine":r["end"]["line"],"endColumn":r["end"]["character"]});
        if let Some(source)=item.get("source"){ensure!(source.is_string(),"Invalid diagnostic source");d["source"]=source.clone();}
        if let Some(url)=item["codeDescription"].get("href"){ensure!(url.is_string(),"Invalid diagnostic link");d["helpUrl"]=url.clone();}
        if let Some(data)=item.get("data"){d["data"]=data.clone();}
        if let Some(tags)=item.get("tags"){ensure!(tags.as_array().is_some_and(|a|a.iter().all(|n|n.as_u64().is_some())),"Invalid diagnostic tags");d["tags"]=tags.clone();}
        if let Some(related)=item.get("relatedInformation") {
            let values=related.as_array().context("Invalid related information")?.iter().map(|v|{
                let location=&v["location"];let r=range(&location["range"])?;
                Ok(json!({"message":v["message"].as_str().context("Invalid related message")?,"filePath":file(location["uri"].as_str().context("Invalid related URI")?),"startLine":r["start"]["line"],"startColumn":r["start"]["character"],"endLine":r["end"]["line"],"endColumn":r["end"]["character"]}))
            }).collect::<Result<Vec<_>>>()?;d["relatedInformation"]=json!(values);
        }Ok(d)
    }).collect()
}
pub fn ordered(mut values: Vec<Value>) -> Vec<Value> {
    values.sort_by_cached_key(|v| {
        (
            v["filePath"].as_str().unwrap_or("").to_owned(),
            v["uri"].as_str().unwrap_or("").to_owned(),
            v["startLine"]
                .as_u64()
                .or_else(|| v["range"]["start"]["line"].as_u64())
                .unwrap_or(0),
            v["startColumn"]
                .as_u64()
                .or_else(|| v["range"]["start"]["character"].as_u64())
                .unwrap_or(0),
            v.to_string(),
        )
    });
    values.dedup();
    values
}
pub fn locations(value: &Value) -> Result<Vec<Value>> {
    let values = match value {
        Value::Null => vec![],
        Value::Array(a) => a.iter().collect(),
        Value::Object(_) => vec![value],
        _ => anyhow::bail!("Invalid navigation result"),
    };
    let mut results = Vec::new();
    for item in values {
        let link = item.get("targetUri").is_some();
        let u = item[if link { "targetUri" } else { "uri" }]
            .as_str()
            .context("Navigation URI is required")?;
        Url::parse(u)?;
        let r = range(
            &item[if link {
                "targetSelectionRange"
            } else {
                "range"
            }],
        )?;
        let mut location = json!({"uri":u,"filePath":file(u),"range":r});
        if link {
            location["targetRange"] = range(&item["targetRange"])?;
            if let Some(origin) = item.get("originSelectionRange") {
                location["originSelectionRange"] = range(origin)?;
            }
        }
        results.push(location);
    }
    Ok(ordered(results))
}
