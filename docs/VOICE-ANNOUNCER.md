# 战场语音播报

## 边界

语音只回答两类离散事件：

- **遇到了谁**：
  - Boss、精英：按遭遇 ID 播放一条专门制作的完整播报。
  - 普通战斗：按实际同时出现的敌人组合播放一条完整句。
  - 战斗中出现新敌人：通过屏幕阅读器输出完整“增援”文本。
- **谁获得或失去了什么状态**：
  - Buff / Debuff 第一次出现时，把完整本地化文本发送给玩家的屏幕阅读器。
  - 状态被移除时播报结束。
  - 层数与持续时间变化不播报；同一状态叠层不会被误报为重新开始。
- 意图、伤害、攻击段数、位置与防御结果继续全部由音乐表达，语音不重复。

遭遇身份使用离线 OGG；动态状态使用 Tolk 接入 NVDA、JAWS 等屏幕阅读器，同时输出语音和盲文；没有屏幕阅读器时保持静默，不回退 Windows SAPI。AI 模型不随 Mod 发布。

## 运行时

`EncounterAnnouncer` 从 `CombatSetUp` 开始订阅参战单位的 `PowerApplied` / `PowerRemoved`，并处理 `CombatBegan`、`CreaturesChanged` 和 `CombatEnded`：

1. `CombatSetUp` 提前接入状态事件，因此 `BeforeCombatStart` 阶段施加的开场状态也不会漏报。
2. `CombatBegan` 记录初始敌人；Boss/精英优先查完整遭遇播报，缺失时降级为普通名称拼接。
3. `CreaturesChanged` 只播本场战斗中第一次出现的存活敌人；死亡、移位和热重载不会误报增援。
4. 只播可见且分类为 Buff / Debuff 的 Power；单位死亡时批量清理的状态保持静默。
5. 状态组成完整文本，如“你受到易伤”“蜂群术士的虚弱结束”，排到遭遇 OGG 播完之后交给 Tolk。
6. Tolk 仅使用玩家正在运行的 NVDA / JAWS 等屏幕阅读器及其声线、语速和盲文设置；否则保持静默。
7. `VoicePlayback` 只负责遭遇身份 OGG；切换游戏语言时重新加载对应清单，缺少时回退 `eng`。

`sts_announce` 重播当前遭遇身份；`sts_speak <文本>` 直接测试屏幕阅读器/SAPI。热重载进入已有战斗时只建立基线，不会把当前敌人当成增援。

## 资源格式

目录：

```text
SoundTheSpire/voices/{game-locale}/
  manifest.json
  common/encounter.ogg
  common/reinforcements.ogg
  encounters/boss/{encounter-id}.ogg
  encounters/elite/{encounter-id}.ogg
  encounters/rosters/{roster-hash}.ogg
```

语言目录直接使用游戏代码：`eng`、`zhs`、`zht`、`jpn`、`kor` 等。清单中的键使用游戏 `EncounterModel.Id.Entry` 或 `MonsterModel.Id.Entry`，不要使用显示名。`text` 是生成与审校文本，运行时只读取 `file`。

运行时把当前敌人的稳定 ID 排序后组成 roster key，优先查完整编队句；Boss/精英专属句优先级更高。完整句缺失或出现动态增援时，通过 Tolk 输出完整本地化文本，不再拼接孤立怪名音频。

当前简体中文矩阵由游戏运行时枚举 `ModelDb.AllEncounters`：115 个遭遇各采样 1024 个 RNG 种子，得到 134 种唯一初始编队。文案中的怪物名直接来自游戏本地化。

### 中文口述词表

UI 显示名不一定适合朗读。`monsters.*.matrixText` 只改变播报文案，不改变游戏显示：

- 四字及以下默认保留官方名称；未明确列出的长名称也暂时保留。
- 树叶/树枝史莱姆的`（小）/（中）`改为前置的“小型/中型”。
- 盛碗虫采用后缀分类；生成文本写作同音的“成碗虫·石、成碗虫·丝、成碗虫·卵、成碗虫·蜜”以防 TTS 误读，游戏显示仍保留官方“盛碗虫”。
- 指定精英/Boss简称：感染棱柱→棱柱，蜂群术士→养蜂人，残杀千足虫→千足虫，永世沙漏→沙漏，实验体编号→实验体；女王保持原名。
- 残杀千足虫的三个战斗节点合并为一个“千足虫”，不读成三只怪。
- 同族队伍共享一次族名前缀：劫掠者组合读作“劫掠者队伍：斧手、弩手与刺客”等，不重复每个成员的“劫掠者”。
- 纯史莱姆编队同样合并前缀，如“史莱姆队伍：小型树枝、中型树枝与小型树叶”；与其他怪物混编时保留完整“史莱姆”名称。
- 邪教徒统一口述为“咔咔”，虔诚雕刻师为“大咔咔”；逐句指令固定第一个“咔”为一声、第二个为四声。
- 外骨骼虫口述为“蟑螂”。
- 四只花园幽灵鳗的精英编队简称“四鳗”，最终生成文本写作同音的“四漫”以获得自然连读。
- 机甲骑士口述为“大型机宝”；女王编队整句为“灯和女王”。
- 盛碗虫族母口述为“成碗虫母虫”，乐加维林族母口述为“乐嘉祖母”，避免两者混淆。
- 多尼斯异鸟口述为“异鸟”，啃咬机最终口述为“小型机宝”，异蛙寄生虫使用同音生成文本“区区”，稳定读作 `qū qū`。
- `rosterOverrides` 处理无法靠单怪文字替换表达的特殊编队。

`sync_roster_catalog.py` 仅删除口述文本实际变化的旧 OGG；满足要求的音频不会重新生成。

### 卡名资产

`sts_card_voice_catalog` 从当前本地化导出全部卡牌 ID 与标题，`sync_card_catalog.py` 合并到 manifest，Qwen 使用 `--cards-only` 在后台生成 `cards/*.ogg`。当前版本共 609 个卡牌 ID、601 个唯一中文标题。卡名使用独立的中性系统播报提示：平稳、无情绪、不强调、不拉长；不复用遭遇播报风格。语速由模型提示控制，不做 `atempo` 后处理。最终 609 条共 7.95MB，时长 0.60–2.67 秒。

`CardAnnouncer` Patch `NCardHolder.OnFocus`：鼠标悬停、键盘和手柄焦点统一触发卡名，切换卡牌时中断上一条；同一卡 250ms 内防抖。缺少预生成 OGG 时通过 Tolk/SAPI 朗读本地化标题。`sts_card_voice <ID|标题>` 用于诊断。

声音设置页提供“Sound the Spire 卡牌名称播报”独立开关，默认开启；关闭时立即停止当前卡名，不影响遭遇语音和音乐。设置持久化到 `user://sound_the_spire_card_voice.cfg`，控制台 `sts_card_announce [on|off]` 可查询或切换。

## AI 生成

遭遇语音使用 [Qwen3-TTS](https://github.com/QwenLM/Qwen3-TTS) `1.7B-CustomVoice` 的固定中文女声 Serena（Apache 2.0）。生成脚本：

```powershell
python tools/voice-generation/generate_qwen.py `
  SoundTheSpire/voices/zhs/manifest.json `
  --model C:\path\to\Qwen3-TTS-12Hz-1.7B-CustomVoice
```

从当前游戏更新全部可能编队：

```powershell
.\scripts\hot.ps1
.\scripts\sts.ps1 "sts_roster_catalog C:\path\roster-catalog.json 1024"
python tools/voice-generation/sync_roster_catalog.py `
  C:\path\roster-catalog.json `
  SoundTheSpire/voices/zhs/manifest.json
python tools/voice-generation/generate_qwen.py `
  SoundTheSpire/voices/zhs/manifest.json `
  --model C:\path\to\Qwen3-TTS-12Hz-1.7B-CustomVoice `
  --rosters-only
```

脚本生成后通过 FFmpeg：

- 只裁开头静音，绝不裁尾；
- 统一为 48 kHz 单声道；
- 归一化到约 -16 LUFS、-1.5 dBTP；
- 补 150ms 尾部静音；
- 编码为 Ogg Vorbis。

禁止对发布语音使用机械语速归一。早期 `normalize_roster_rate.py` 的 `silenceremove + atempo` 方案会破坏自然停顿、连读和尾音，现已弃用；只保留它作为实验/分析工具。发布资产必须从模型端通过固定声线、确定性采样和提示词控制节奏。

生成或替换 OGG 后无需重启游戏：

```powershell
.\scripts\hot.ps1
```

热构建会递归复制 `SoundTheSpire/voices/`，随后执行 `sts_reload`。新一代热模块重新读取当前语言的 manifest，并清理旧播放队列；当前战斗和跑局保持不变。

默认提示要求成熟、冷静、神秘、克制的黑暗奇幻旁白，明确禁止电竞腔、广告腔和模仿现有演员。

## 文案与审校

- 普通名称必须忠于游戏当前本地化。
- 专属句可以有氛围，但身份必须清晰，不能透露意图或数值。
- 状态文本直接使用游戏当前本地化名称；状态开始/结束不朗读层数和剩余回合。
- 同一语言保持一个稳定播报员人格；Boss 可以更长、更有压迫感，普通怪保持短促。
- 每条音频都要检查读音、重音、爆音、底噪、首尾切口和游戏音乐下的可懂度。
- 免费创意工坊发布也按可商用标准审查模型、权重、数据集和参考声线许可。

## 初版状态

- 已完成：Boss/精英专属优先、134 种普通遭遇编队矩阵、增援回退、Qwen 批量生成、Tolk 屏幕阅读器/SAPI 状态播报、热更与打包复制。
- 待资产制作：完成人工验收 134 条编队整句；补齐更有风格的精英/Boss 专属句；为其他语言增加本地化清单。
