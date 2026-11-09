# TURZX Pro 4.6 v1.1.1

本版本是 USB 通信稳定性补丁，重点修复屏幕断电重启及持续实时显示时的连接问题。

## 修复与改进

- 修复屏幕断电重启后首次启动实时显示可能提示 `device did not return a command response within 2.5 seconds` 的问题。
- 修正冷启动握手使用的时间字段和 DES PKCS7 填充方式，与原厂协议保持一致。
- 增加 USB 管道复位、迟到响应过滤、完整写入重试和启动设备就绪等待。
- 图片、视频及实时主题帧不再强制等待可选响应，避免设备已接收画面但软件误报失败。
- 实时主题使用持久 USB 会话，并在通信异常时自动重新连接。

推荐下载 `TURZX-Pro-v1.1.1-Setup.exe`。免安装使用可下载 `TURZX-Pro-v1.1.1-win-x64.zip`。

`TURZX-Pro-v1.1.1-Theme-black1.zip` 是可独立下载的黑白监控主题；安装包和便携版中也包含当前保存的主题预设。

本版本未使用商业代码签名证书，Windows 可能显示“未知发布者”。请从本仓库 Release 下载，并使用 `SHA256SUMS.txt` 校验文件完整性。
