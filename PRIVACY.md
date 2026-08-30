# 隐私说明

TURZX Pro 4.6 不包含用户统计、广告 SDK 或遥测模块。

## 本机数据

软件会在 `%APPDATA%\TurzxControl` 中保存界面设置、传感器映射、天气城市、主题和 AI 接口配置。这些文件不会被项目自动上传。

AI API Key 使用 Windows DPAPI 的 CurrentUser 范围加密，只能由同一 Windows 用户解密。卸载软件不会自动删除用户主题和设置；如需彻底清理，可在退出软件后手动删除上述目录。

## 网络请求

- 天气组件使用 Open-Meteo 获取城市坐标和天气。
- AI 创意功能只在用户主动点击生成时访问用户自行配置的接口。
- 其余 USB 输出与硬件传感器采集均在本机完成。

请勿在 Issue、截图或日志中公开自己的 API Key、带有个人标识的接口地址或 `%APPDATA%\TurzxControl` 配置文件。

