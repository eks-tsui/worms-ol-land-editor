# 地图与资源格式

本文说明编辑器当前使用的数据模型，不是对所有文件变体的兼容保证。

## DAT 地图

地图使用 `LND\x1A` 文件头，依次包含文件总长度、宽度、高度、封闭标志、两组坐标点、四个图像块和两段长度前缀 ASCII 名称。整数以当前实现的二进制读写规则编码。

```text
LND\x1A
int32 fileLength
int32 width
int32 height
int32 closedMapFlag
int32 primaryPointCount
primaryPointCount × { int32 x, int32 y }
int32 secondaryPointCount
secondaryPointCount × { int32 x, int32 y }
IMG terrain
IMG collisionMask
IMG auxiliaryMask
IMG previewMask
byte firstNameLength
ASCII firstName
byte secondNameLength
ASCII secondName
```

主编辑流程固定为 1920×696。地形使用 8 位索引颜色，保存会限制调色板；碰撞与辅助遮罩使用 1 位像素，预览遮罩为 240×87。PNG 实体判定为 alpha≥1，当前流程保持预览遮罩为空。

GUI 保存将名称字段设置为编号对应的地图资源目录和所选水资源路径。基础 `--frompng` 命令不会自动完成这一步或建立出生点。

读写可能发生量化、调色板重映射和资源规范化，不应将“能够回读”解释为“字节完全相同”。

## DIR 资源包

资源包使用 `DIR\x1A` 文件头，包含资源块、数据结束位置、目录起始位置及目录索引。

当前写入以已有资源包的目录索引为基础，替换资源内容后更新偏移和长度。它不支持根据任意散装文件从零建立索引。GUI 编辑的五个常用资源为 `gradient.img`、`back.spr`、`debris.spr`、`soil.img`、`grass.img`。

其他资源条目与调色板关系同样影响保存结果，不建议手工删除不认识的条目。

## IMG 图像

图像块使用 `IMG\x1A` 标识，包含块长度、名称、flags、可选调色板、宽高和像素数据。flags 包含每像素位数及调色板信息。

主地形和资源图像采用不同的转换路径，不能把主地图的 alpha 判定规则推广为所有资源的透明度规则。

## SPR 动画

SPR 包含调色板、动画参数、帧表及每帧像素数据。画布和帧边界可能不同。

- PNG 可以替换 SPR 图像，GIF 可以提供多帧输入。
- `back.spr` 要求输入宽度为 640；`debris.spr` 要求输入宽高不超过 80。
- 普通 SPR 以已有资源的替换画布尺寸校验。
- GIF 导出使用统一 120ms 帧延迟，不能保证原时序无损保留。

## 兼容输入

兼容导入会结合文件头、扩展名和附近目录识别适用的 IMG / DAT / WAM / Level.dir 组合，并转换为编辑器使用的地图和资源格式。实现中的压缩数据解码不等于提供对应名称的通用图片导入功能，例如不提供通用 BMP 文件入口。

某些无法解析的兼容 SPR 资源会回退为空白占位。导入成功不代表每项资源都完整还原，请逐项检查资源预览和导出结果。
