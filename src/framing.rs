use anyhow::{Context, Result, ensure};
use serde_json::Value;
use tokio::io::{AsyncBufRead, AsyncBufReadExt, AsyncReadExt, AsyncWrite, AsyncWriteExt};
pub const MAX_MESSAGE: usize = 16 * 1024 * 1024;
pub async fn read_lsp<R: AsyncBufRead + Unpin>(reader: &mut R) -> Result<Option<Value>> {
    let mut length = None;
    let mut size = 0;
    loop {
        let mut line = String::new();
        let count = reader.take(8193).read_line(&mut line).await?;
        if count == 0 {
            ensure!(size == 0, "Truncated LSP header");
            return Ok(None);
        }
        size += count;
        ensure!(size <= 8192, "LSP header exceeds limit");
        if line == "\r\n" || line == "\n" {
            break;
        }
        let (key, value) = line
            .trim_end()
            .split_once(':')
            .context("Malformed LSP header")?;
        if key.eq_ignore_ascii_case("Content-Length") {
            ensure!(length.is_none(), "Duplicate Content-Length");
            length = Some(value.trim().parse::<usize>()?);
        }
    }
    let length = length.context("Missing Content-Length")?;
    ensure!(
        length > 0 && length <= MAX_MESSAGE,
        "Invalid LSP message length"
    );
    let mut bytes = vec![0; length];
    reader.read_exact(&mut bytes).await?;
    Ok(Some(serde_json::from_slice(&bytes)?))
}
pub async fn write_lsp<W: AsyncWrite + Unpin>(writer: &mut W, value: &Value) -> Result<()> {
    let bytes = serde_json::to_vec(value)?;
    ensure!(bytes.len() <= MAX_MESSAGE, "LSP message exceeds limit");
    writer
        .write_all(format!("Content-Length: {}\r\n\r\n", bytes.len()).as_bytes())
        .await?;
    writer.write_all(&bytes).await?;
    writer.flush().await?;
    Ok(())
}
