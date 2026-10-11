# PmmCamKit

Camera tools for MikuMikuDance projects (PMM, MMD 9.x). Windows, no installation (uses the .NET Framework 4 built into Windows). UI in 中文 / 日本語 / English.

- **Merge camera rig**: move a camera-rig model (with its bone keyframes) and the camera from one PMM into a target PMM. The target PMM's content stays unchanged, outside-parent and camera-follow bindings are remapped (matched by bone name), and a matching `.emm` keeps all MME effect assignments.
- **Batch camera follow**: bind camera bone follow on all camera keyframes from the first bound one.

MMD 工程相机工具：把相机骨合并进目标工程（保留特效分配）、相机追踪批量绑定。
MMD プロジェクト用カメラツール：カメラボーンを統合先プロジェクトに統合（エフェクト割り当てを保持）、カメラのボーン追従を一括設定。

## Download

Get the latest version from [Releases](https://github.com/CicloO/PmmCamKit/releases/latest): download `PmmCamKit_<version>.zip`, unzip, and double-click `PmmCamKit.exe`. Usage (中文 / 日本語 / English): [`docs/README.txt`](docs/README.txt).

## Build

Run `src/build.bat` (uses the C# compiler bundled with Windows). Output: `build/PmmCamKit.exe`.

Repository layout: `src/` source, `docs/` the user guide shipped in the zip, `LICENSE`.

## License

[MIT](LICENSE) © 2026 CicloO

## Credits

PMM format reference: [Scallion](https://github.com/paralleltree/Scallion) (MIT).
