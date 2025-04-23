# Studio

# NOTICE: Project has been temporarily suspended till further notice.

Check out the [original AssetStudio project](https://github.com/Perfare/AssetStudio) for more information.

Note: Requires Internet connection to fetch asset_index jsons.
_____________________________________________________________________________________________________________________________
How to use:

Check the tutorial [here](https://gist.github.com/Modder4869/0f5371f8879607eb95b8e63badca227e) (Thanks to Modder4869 for the tutorial)
_____________________________________________________________________________________________________________________________
CLI Version:
```
Description:

Usage:
  AssetStudioCLI <input_path> <output_path> [options]

Arguments:
  <input_path>   Input file/folder.
  <output_path>  Output folder.

Options:
  --silent                                                Hide log messages.
  --type <Texture2D|Sprite|etc..>                         Specify unity class type(s)
  --filter <filter>                                       Specify regex filter(s).
  --game <BH3|CB1|CB2|CB3|GI|SR|TOT|ZZZ> (REQUIRED)       Specify Game.
  --map_op <AssetMap|Both|CABMap|None>                    Specify which map to build. [default: None]
  --map_type <JSON|XML>                                   AssetMap output type. [default: XML]
  --map_name <map_name>                                   Specify AssetMap file name.
  --group_assets_type <ByContainer|BySource|ByType|None>  Specify how exported assets should be grouped. [default: 0]
  --no_asset_bundle                                       Exclude AssetBundle from AssetMap/Export.
  --no_index_object                                       Exclude IndexObject/MiHoYoBinData from AssetMap/Export.
  --xor_key <xor_key>                                     XOR key to decrypt MiHoYoBinData.
  --ai_file <ai_file>                                     Specify asset_index json file path (to recover GI containers).
  --version                                               Show version information
  -?, -h, --help                                          Show help and usage information
```
_____________________________________________________________________________________________________________________________
NOTES:
```
- in case of any "MeshRenderer/SkinnedMeshRenderer" errors, make sure to enable "Disable Renderer" option in "Export Options" before loading assets.
- in case of need to export models/animators without fetching all animations, make sure to enable "Ignore Controller Anim" option in "Options -> Export Options" before loading assets.
```
_____________________________________________________________________________________________________________________________
Special Thank to:
- Perfare: Original author.
- Khang06: [Project](https://github.com/khang06/genshinblkstuff) for extraction.
- Radioegor146: [Asset-indexes](https://github.com/radioegor146/gi-asset-indexes) for recovered/updated asset_index's.
- Ds5678: [AssetRipper](https://github.com/AssetRipper/AssetRipper)[[discord](https://discord.gg/XqXa53W2Yh)] for information about Asset Formats & Parsing.
- mafaca: [uTinyRipper](https://github.com/mafaca/UtinyRipper) for `YAML` and `AnimationClipConverter`. 



Thanks to the original author Raztools for sharing the source code.感谢原作者Raztools分享的源码             2025/2/15
2025/4/24适配原神、崩坏星穹铁道、绝区零，除了未定事件簿不能解，米哈游的大多数游戏都已经支持，崩坏三是2月23适配的，然后就是物华弥新，我在2月份汉化的程序是能解物华弥新的，猜测当时是dnspy反编译修改的，这就导致源码仍是错误的，所以会出现你们使用我汉化的成品能正常解物华弥新，但是使用源码编译的却仍然解不开物华弥新，这里我给大家道个歉，疏忽了，本次更新重点适配了3个米哈游的游戏，修复了物华弥新的解密方法，修改了FakeHeader读取字节数量，由原来的0x1000改成0x2710(也就是1万个字节，以此解决敢达争锋对决使用FakeHeader失效的问题)。

本次更新发现绝区零不能汉化，必须得用ZZZ来表示，一汉化就无法解包，懒得排查问题了，而崩坏三、原神、崩坏星穹铁道和未定事件簿则使用静态映射的方法汉化为中文的，以免出现像绝区零那样汉化后无法解包的报错。

其他的话，归龙潮这个游戏未适配，有的版本的assetstudio无法完美解包这个游戏，只能解开一部分，所以这个就暂时不集成适配了。

未来也许会适配更多的游戏。


——偷吃布丁的涅普缇努

