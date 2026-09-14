# 第三方内容

## pack_godot_pck.py

本目录下的 `pack_godot_pck.py` 复制自 [Quorafind/STS2-Mod-Template](https://github.com/Quorafind/STS2-Mod-Template)，
用于把 `assets/` 打包成 Godot PCK（pack version 3 / engine 4.5.1）。

该模板以 MIT 许可证发布，原文如下：

```
MIT License

Copyright (c) 2026 Boninall

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 游戏内引用

本 mod 通过反射引用《Slay the Spire 2》的私有成员（`NRestSiteRoom._runState`、`NRestSiteRoom.Header`）与
Harmony 运行时补丁。游戏本体、程序集及文本版权归 MegaCrit 所有，本 mod 仅用于个人游玩与学习。
