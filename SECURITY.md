# 安全说明

## 硬件传感器权限

普通界面始终以当前用户权限运行。需要底层硬件访问时，用户可在设置中安装官方 PawnIO 驱动和 TURZX Sensor Service。该服务以 LocalSystem 运行，只通过受限的本机命名管道向界面提供传感器读数。

驱动和服务不会在安装 TURZX Pro 时自动部署，必须由用户在软件内明确触发并确认 UAC。

## API 密钥

仓库、安装包和默认配置中不包含 API 密钥。用户填写的密钥使用 Windows DPAPI 加密后保存在本机。

## 报告问题

提交安全问题或普通 Issue 前，请移除以下内容：

- API Key、Token 和 Authorization 请求头
- 私有 API 地址或业务空间标识
- Windows 用户名和本机绝对路径
- `%APPDATA%\TurzxControl` 下的完整配置文件

