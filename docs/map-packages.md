# Map ZIP packages / 地图 ZIP 包

## Scope / 范围

A package transfers one saved map and its complete `Level.Dir` resource archive. It does not include shared water assets, unrelated files in the map folder, or any other map. Export requires saving pending changes first; cancelling that save cancels export.

地图包用于传递一张已保存地图及完整 `Level.Dir`。不包含共享水素材、地图目录的其他文件或其他地图。存在未保存修改时必须先保存，取消保存即停止导出。

The output filename must end in `.zip`. The outer ZIP filename is only a suggestion (`land01.zip`, `land100.zip`, etc.). The manifest and validated internal paths identify the source map; renaming the ZIP does not change its map number.

输出文件名必须以 `.zip` 结尾。ZIP 外部文件名只是建议值，真正的来源编号由清单及内部路径共同确定。改名 ZIP 不会改变地图编号。

## Version 1 layout / 第1版布局

Exactly three regular-file entries are required, using `/` separators:

必须且只能包含三个普通文件条目，路径分隔符为 `/`：

```text
manifest.json
Data/land01.dat
Data/Mfx/Tmap01/Level.Dir
```

The manifest has these fields:

| Field | Meaning |
| --- | --- |
| `format` | `worms-ol-map-package` |
| `version` | Integer `1`; independent of the application version |
| `sourceMapNumber` | Source map number, `1–999` |
| `width`, `height` | `1920`, `696` |
| `land.path`, `resources.path` | Exact DAT and DIR paths matching the source number |
| `land.length`, `resources.length` | Uncompressed byte lengths |
| `land.sha256`, `resources.sha256` | SHA-256 digests of the payloads |
| `water.path` | The DAT's external water resource reference |
| `water.included` | Must be `false` |

Payload lengths, hashes, dimensions, resource paths and binary structure are checked before asking to replace the current editing contents. Hashes detect corruption, not trust: a malicious author can construct matching hashes. Only import packages from sources you trust.

替换当前编辑内容前会检查载荷长度、哈希、尺寸、资源路径和二进制结构。哈希用于检查完整性，不能证明来源可信，请仅导入可信来源的地图包。

The DIR hash-table prefix is preserved, not semantically reinterpreted. Structural validation follows the sequential index entries and binary variants supported by this editor; it does not establish compatibility with every possible consumer.

DIR 哈希表前缀原样保留，不重新解释其全部语义。结构校验针对编辑器读取的顺序索引条目和受支持二进制变体，不代表所有使用方的兼容性验收。

Legacy TXT index records may be empty, shorter than an image header, or reference bytes also used by another resource. They are retained as bounded opaque byte spans, never opened as file paths. IMG and SPR records must still be structurally valid and must not overlap each other. The sum of all indexed payload lengths is limited to 64 MiB, including repeated references.

旧 DIR 的 TXT 索引记录可以为空、短于图像文件头，或引用其他资源共用的字节。它们按有边界的原始字节保留，不作为文件路径打开。IMG 和 SPR 仍需通过结构校验，彼此不能重叠；所有索引载荷长度之和（包括重复引用）最多为64 MiB。

ZIP export copies the saved DAT and DIR bytes without converting their binary formats. ZIP import preserves the complete DIR byte for byte and changes only the DAT's numbered resource reference and, if necessary, its length. Editing and saving is a separate operation: it may remap palettes or rebuild resource offsets within the existing formats. These checks do not replace testing in the target game.

ZIP 导出不转换已保存 DAT 和 DIR 的二进制格式。ZIP 导入逐字节保留完整 DIR，仅改写 DAT 的地图编号引用及必要的长度字段。编辑并保存是另一项操作，可能在原格式内重映射调色板或重建资源偏移；结构检查不能代替目标游戏实测。

## Number mapping / 编号映射

Importing source map 1 into selected map 100 maps the in-memory document to the following paths. No game files are written until explicit Save:

把来源编号1导入当前编号100时，内存文档映射到以下目标路径；只有明确保存后才写入游戏文件：

```text
Data/land100.dat
Data/Mfx/Tmap100/Level.Dir
```

The DAT's numbered terrain-resource reference (`NameA`) is rewritten in memory to map 100 and its total-length field is adjusted if necessary. Terrain pixels, masks, spawn points, settings and the external water reference (`NameB`) are preserved. `Level.Dir` is copied intact; its indexed paths are relative resource names rather than map-number paths.

导入时在内存中改写 DAT 的编号地形资源引用 `NameA`，必要时更新文件长度字段。地形像素、遮罩、出生点、地图选项和外部水引用 `NameB` 保持不变；`Level.Dir` 原样复制，其内部索引使用相对资源条目名，不是地图编号路径。

Import changes no game-directory files. Explicit Save changes only the selected DAT and DIR; no shared water directory is written or changed. If the referenced water is unavailable at the destination, its preview is unavailable until the user supplies the appropriate authorized resources. Files outside the two destination paths remain untouched, including extra files in the target Tmap folder.

导入不会更改任何游戏目录文件；明确保存仅写入所选 DAT 和 DIR，不会写入或修改共享水目录。目标缺少所引用的水资源时，需自行提供有权使用的对应素材才能预览。除了两个目标文件，不会修改其他文件，目标 Tmap 目录中的额外文件也会保留。

## Limits / 限制

- Three ZIP entries, with no extra directory entries; ZIP64 is unsupported.
- Manifest: at most 16 KiB. DAT: at most 32 MiB. DIR: at most 64 MiB.
- Total uncompressed size: at most 96 MiB plus the manifest. ZIP file: at most 97 MiB.
- Entries over 1 MiB with a compression ratio over 2000 are rejected.
- DAT images must use the fixed terrain and mask dimensions.
- Resource images: at most 8192 pixels per axis and 16 Mi pixels. Sprites: at most 255 frames and 64 Mi cumulative frame pixels.
- Expanded sprite canvases: at most 16 Mi pixels across all frames of one SPR. The complete DIR has a 32 Mi allocation-pixel budget: each sprite is charged the larger of its total raw frame pixels and expanded canvas pixels, including transparent canvas area.
- DIR validation supports the editor's IMG, SPR and TXT resource entries. Unsupported binary variants are rejected rather than silently omitted or rewritten.

只接受严格的本工具地图包，不支持导入任意游戏目录 ZIP。空目录条目、未知文件、超出限制或不支持的二进制变体会被拒绝。

## Write safety / 写入安全

Archive entries are read through bounded streams; their paths are never passed to a general directory-extraction routine. Absolute paths, traversal components, backslash archive paths, duplicate names, symbolic links/reparse points and unsafe destination ancestors are rejected.

导入通过有上限的流读取 ZIP，不按归档路径直接解压。绝对路径、路径穿越、反斜杠归档路径、重复条目、符号链接/重解析点以及不安全的目标祖先路径都会被拒绝。

Import validation and number mapping produce an immutable in-memory package. Candidate models and previews are prepared before replacing the editor state. Cancelling or failing this preparation leaves the previous editor state and disk untouched. An accepted import is dirty and follows the normal unsaved-change prompts. Neither importing nor viewing the new map stages destination files.

导入校验和编号映射得到不可变的内存地图包，候选模型和预览准备成功后才替换编辑状态。取消或准备失败时，旧编辑状态和磁盘均不变。成功导入标记为未保存，并沿用未保存修改确认；导入或查看新地图都不会暂存目标文件。

Explicit Save persists the current editor document. For an unedited imported package, it uses the already validated mapped DAT and full original DIR bytes without rereading the ZIP or substituting a template. Further editing invalidates this original-byte snapshot and uses normal model serialization. Both save paths stage both payloads, back up originals, and then replace the destinations. An ordinary commit failure triggers restoration; failed recovery retains backups and reports where they are. This is not a crash-proof filesystem transaction. Keep independent backups.

明确保存时才持久化当前文档。刚导入且未进一步编辑时，使用已验证的映射 DAT 和完整原始 DIR 字节，不重新读取 ZIP，也不回退替换为模板。后续编辑会使原始字节快照失效，改为正常模型序列化。两种保存路径都先暂存两个文件、备份原件，再替换目标；普通提交失败会尝试恢复，恢复失败则保留备份并报告位置。这不是防断电的文件系统事务，请保留独立备份。
