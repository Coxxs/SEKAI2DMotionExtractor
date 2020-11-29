# SEKAI2DMotionExtractor
一个针对**特定游戏** Live2D Motion 资源的提取工具。

通过读取 `.moc3` 获得参数列表，提取 `.anim` 文件、绑定参数并转换回 `.motion3.json`。Motion 以外的其他资源需自行提取。

**程序中通过硬编码的 `/live2d/motion` 关键词寻找指定的资源，因此不直接适用于其他游戏。**

基于 [UnityLive2DExtractor](https://github.com/Perfare/UnityLive2DExtractor) 修改而成，版权归原作者所有。

## Usage
拖放（反混淆后的）按需下载资源文件夹到exe上，将在文件夹所在目录生成`Live2DOutput`目录

Drag and drop the (deobfuscated) on demand asset data folder to the exe, and the `Live2DOutput` directory will be generated in the directory where the folder is located

## Command-line
UnityLive2DExtractor.exe live2dfolder

## Requirements
- [.NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)
