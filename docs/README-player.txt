Sound the Spire —— 用耳朵玩《杀戮尖塔 2》

这个模组把战斗信息变成音乐：敌人要做什么、你挡不挡得住，都能听出来。
默认开启"训练遮罩"：战斗中隐藏敌人意图、血量和护甲数字、伤害数字、卡牌数值，逼你用声音判断。
卡牌图鉴和战斗外的牌组照常显示数值，可以在那里学习。


【安装】

一键安装（推荐）：在发布页 https://github.com/MuGdxy/SoundTheSpire/releases 下载 install.bat，双击运行。
它会自动找到游戏、下载最新版并装好；以后再运行一次就是更新。
也可以在 PowerShell 里粘贴：
   irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/install.ps1 | iex

手动安装：
1. 打开游戏目录：Steam 库里右键《杀戮尖塔 2》→ 管理 → 浏览本地文件。
2. 如果没有 mods 文件夹，新建一个。
3. 把压缩包里的 SoundTheSpire 文件夹整个放进 mods，变成：
   Slay the Spire 2\mods\SoundTheSpire\SoundTheSpire.dll
4. 启动游戏。若游戏询问是否加载模组，选择加载。

开模组时游戏使用一份单独的存档（modded），原来的进度不受影响，也不会出现在这里。
卸载：双击发布页里的 uninstall.bat，或在 PowerShell 里粘贴：
   irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/uninstall.ps1 | iex
也可以手动删除 mods\SoundTheSpire 文件夹。卸载后回到原来的存档。创意工坊订阅的，在 Steam 里取消订阅。

联机：所有玩家都要装同一个版本的本模组。


【按键】

R    重听本回合汇总
F9   开 / 关训练遮罩（看见数值、意图）
F8   声音测试（钢琴左、吉他中、鼓右）

主菜单中央的"声音教学关"：直接开一局独立、不存档的新局并进入教学关。正常游戏局内不能进入教学关；打赢教学战斗后直接回到主菜单。

鼠标移到某只怪身上，单独听它的意图。
鼠标移到自己的角色身上，重听当前防御端状态；已经完全挡住时会播放解决段。
鼠标、键盘或手柄选中卡牌时，会直接播报卡名。

进入战斗时会用预生成语音播报遇到的怪物；中途出现新怪会播报"增援"。
Buff / Debuff 的开始和结束使用玩家当前的 NVDA、JAWS 等屏幕阅读器直接朗读，并同步输出到盲文设备；
没有运行屏幕阅读器时，Windows 版自动使用系统 SAPI 声音。

声音设置里有独立的"Sound the Spire 乐段音量"：范围 0–400%，新安装默认 100%。
同一页的"Sound the Spire 卡牌名称播报"可单独开启或关闭卡名语音，默认开启。
每首曲目的 Profile 会自动补偿与背景音乐的基础响度差，这个滑块只用于个人微调。
游戏的主音量仍然控制它；游戏音乐音量只控制背景音乐。


【怎么听】

每个回合开始：
1. 敌人从左到右依次出声，一只一小节。低伤是单音，中伤是根音+五度，高伤是三和弦，超高伤是三和弦+低音根音；攻击越重也越低沉有力。多段攻击是连续扫弦并叠加打击乐，轻到重依次使用三角铁、踩镲、军鼓、通鼓、底鼓+吊镲。
   防御、增益、减益各有不同的音型。
2. 最后一小节是防御：这回合结束时你会挨多少打。
   完全挡住：悬着的和弦落回明亮的大三和弦。
   挡不住：伤得越重越刺耳，最重时是一团完全不协和的撞击。

回合中，每次出牌、击杀、加护甲让结果改变，都会重新响起防御和弦；
变好时从旧和弦连音滑到新和弦，连续出牌会连成一条线。

第一次玩建议先在主菜单点"声音教学关"走一遍。


【致谢】

音色：GeneralUser GS SoundFont（S. Christian Collins），许可见 soundfonts 文件夹。
合成：MeltySynth（MIT）。
屏幕阅读器桥接：Tolk（LGPLv3），许可见模组目录中的 Tolk-LICENSE.txt 与 NVDA-LICENSE.txt。
