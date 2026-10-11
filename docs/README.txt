PmmCamKit 1.0
MMD 工程（PMM）相机工具 / MMD プロジェクト（PMM）用カメラツール / Camera tools for MMD projects (PMM)
https://github.com/CicloO/PmmCamKit

======================================================================
中文
======================================================================
双击 PmmCamKit.exe 运行，无需安装（使用 Windows 自带的 .NET Framework 4）。
支持 MMD 9.x 的 PMM。原文件不会被修改，结果另存为新文件。第一次使用请先用工程副本测试。
右上角可切换界面语言（中文 / 日本語 / English），选择会被记住。

【合并相机骨】
把「相机骨 PMM」里的相机骨模型（含骨骼关键帧）和相机移入「目标 PMM」（做到一半、已配好特效的工程）。
1. 选择「目标 PMM」和「相机骨 PMM」，也可以把文件拖到输入框里。
2. 要移入的模型：默认只勾选像相机骨的模型（名称或骨骼名带 カメラ / camera / 镜头 / 相机 等，且骨骼不超过 120 根）。
3. 「用相机骨 PMM 的相机替换目标 PMM 的相机」默认开启。
4. 绑定替换表：相机骨模型的外部亲、相机追踪所引用的其他模型/骨骼，要对应到目标 PMM 的哪个模型/骨骼。
   - 表中显示原模型、原骨骼、被谁引用、帧范围、关键帧数。
   - 目标 PMM 有同名模型时自动对应同名骨骼；没有时在含同名骨骼的模型中选骨骼最多的（通常是角色），并标「请确认」。
   - 关键帧中途换了目标时，每个目标单独一行，可分别指定。
   - 可在下拉框里改目标，或选「无（解除）」。同名模型以 [ID n] 区分。
5. 点「合并」，输出默认为目标 PMM 旁的「原名_合并.pmm」。
- 移入的模型接在模型列表末尾，目标 PMM 原有内容逐字节保持不变。
- 同时生成同名 .emm：保留目标 PMM 的全部特效分配，并登记新模型（主特效页为 MMD 标准绘制，其他特效页隐藏），MME 不会再弹「无法获取特效分配」。
- 相机骨 PMM 若是在别的电脑上保存的，找不到的模型文件会自动按文件名在两个 PMM 的文件夹里搜索；仍找不到时，选中模型后点「指定所选模型的文件…」。

【追踪批量绑定】
MMD 的相机「骨骼追踪」设置保存在每个相机关键帧上，读入 VMD 后全部是未绑定状态。
在 MMD 里给第一个相机关键帧设置骨骼追踪并保存，再选择或拖入 PMM：
- 向后填充：未绑定的关键帧继承前面最近一个已绑定的设置（中途可手动换目标）。
- 全部覆盖：全部改成第一个已绑定的设置。
输出为「原名_追踪.pmm」。也可以直接把 PMM 拖到 exe 图标上。

启动参数（可做成快捷方式）：PmmCamKit.exe --render 目标.pmm --rig 相机骨.pmm

======================================================================
日本語
======================================================================
PmmCamKit.exe をダブルクリックで起動します。インストール不要（Windows 標準の .NET Framework 4 を使用）。
MMD 9.x の PMM に対応。元のファイルは変更せず、結果は新しいファイルに保存します。初めはプロジェクトのコピーで試してください。
右上で表示言語（中文 / 日本語 / English）を切り替えられます（選択は保存されます）。

【カメラボーンを統合】
「カメラボーン PMM」のカメラボーンモデル（ボーンのキーフレームを含む）とカメラを「統合先 PMM」（エフェクト設定済みの作業中プロジェクト）へ移します。
1. 「統合先 PMM」と「カメラボーン PMM」を選びます（入力欄へのドラッグも可）。
2. 移すモデル：カメラボーンらしいモデル（名前やボーン名に カメラ / camera などを含み、ボーン 120 本以下）だけを既定でチェックします。
3. 「統合先 PMM のカメラをカメラボーン PMM のカメラに置き換える」は既定でオン。
4. 割り当ての置き換え：カメラボーンモデルの外部親やカメラ追従が参照している他のモデル／ボーンを、統合先 PMM のどれに割り当てるか。
   - 元モデル・元ボーン・参照元・フレーム範囲・キー数を表示します。
   - 同名モデルがあれば同名ボーンに自動で割り当て、なければ同名ボーンを持つモデルのうちボーン数が最も多いもの（通常はキャラクター）を選び「要確認」と表示します。
   - 途中で参照先が変わる場合は参照先ごとに行が分かれ、個別に指定できます。
   - ドロップダウンで変更するか「なし（解除）」を選べます。同名モデルは [ID n] で区別します。
5. 「統合」を押すと、統合先 PMM の隣に「元の名前_マージ.pmm」を出力します。
- 移したモデルはモデル一覧の末尾に追加され、統合先 PMM の既存内容はバイト単位でそのままです。
- 同名の .emm も作成します。統合先 PMM のエフェクト割り当てをすべて保持し、新モデルを登録します（メインタブは MMD 標準、他のタブは非表示）。MME の「エフェクト割り当てを取得できません」は出なくなります。
- 別の PC で保存されたカメラボーン PMM で見つからないモデルファイルは、2 つの PMM のフォルダからファイル名で自動検索します。それでも見つからない場合は、モデルを選んで「選択したモデルのファイルを指定…」を押してください。

【追従の一括設定】
MMD のカメラの「ボーン追従」はカメラキーフレームごとに保存され、VMD を読み込むとすべて未設定になります。
MMD で最初のカメラキーフレームにボーン追従を設定して保存し、PMM を選ぶかドラッグします：
- 後ろへ埋める：未設定のキーフレームは直前の設定を引き継ぎます（途中で手動で変えた追従先は維持）。
- すべて上書き：すべて最初の設定にします。
出力は「元の名前_追従.pmm」。PMM を exe のアイコンにドラッグしても使えます。

起動引数（ショートカット用）：PmmCamKit.exe --render 統合先.pmm --rig カメラボーン.pmm

======================================================================
English
======================================================================
Double-click PmmCamKit.exe. No installation needed (uses the .NET Framework 4 built into Windows).
Supports MMD 9.x PMM files. Original files are never modified; results are written to new files. Try it on a copy of your project first.
Switch the UI language (中文 / 日本語 / English) at the top right; the choice is remembered.

[Merge camera rig]
Moves the camera-rig model (with its bone keyframes) and the camera from the "camera-rig PMM" into the "target PMM" (your work-in-progress project with effects set up).
1. Choose the target PMM and the camera-rig PMM (or drop the files onto the boxes).
2. Models to move: only models that look like camera rigs ("camera", "カメラ", ... in the name or bone names, at most 120 bones) are checked by default.
3. "Replace the target PMM's camera with the camera-rig PMM's camera" is on by default.
4. Binding targets: where the other models/bones referenced by the rig's outside parents and the camera follow should point in the target PMM.
   - Shows source model, source bone, used by, frame range and key count.
   - If the target PMM has a model with the same name, the same-name bone is used. Otherwise the model with the most bones that has a same-name bone (usually the character) is picked and marked "check".
   - If the target changes mid-way, each target gets its own row and can be set separately.
   - Change targets in the drop-downs, or pick "None (unbind)". Models with the same name are told apart by [ID n].
5. Click "Merge". The default output is "name_merged.pmm" next to the target PMM.
- Moved models are appended to the end of the model list; the target PMM's existing content stays byte-for-byte the same.
- A matching .emm is written too: all effect assignments of the target PMM are kept and the new models are registered (standard MMD shading in the main tab, hidden in other tabs), so MME no longer warns that it cannot get their effect assignment.
- If the camera-rig PMM was saved on another PC, missing model files are searched for by file name in both PMM folders. If still missing, select the model and click "Locate selected model file…".

[Batch camera follow]
MMD stores the camera's bone follow on every camera keyframe; after loading a VMD none of them are bound.
In MMD, set bone follow on the first camera keyframe and save, then choose or drop the PMM:
- Fill forward: unbound keyframes take the nearest earlier binding (targets you changed by hand are kept).
- Overwrite all: every keyframe gets the first binding.
The output is "name_follow.pmm". You can also drop PMM files onto the exe icon.

Command line (for shortcuts): PmmCamKit.exe --render target.pmm --rig rig.pmm

----------------------------------------------------------------------
PMM format reference: Scallion (MIT, github.com/paralleltree/Scallion)

