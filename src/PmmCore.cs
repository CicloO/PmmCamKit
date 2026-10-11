// PMM（MMD 9.x 工程文件，"Polygon Movie maker 0002"）解析与编辑核心。
// 格式参考 Scallion（MIT，paralleltree/Scallion）。
// 注意：外部亲/相机追踪/饰品绑定里的"模型序号"是模型自带的 ID 字段，不是列表位置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PmmCamKit
{
    class PmmException : Exception
    {
        public PmmException(string msg) : base(msg) { }
    }

    class PmmModel
    {
        public int Start, End;          // 模型数据块在文件中的范围
        public int Id;                  // 模型 ID（引用用的序号）
        public string Name, Path;
        public int PathOfs;             // 路径字段（256 字节）的绝对偏移
        public byte[] PathRaw;          // 路径原始字节
        public List<string> Bones = new List<string>();
        public int RenderOfs, CalcOfs;  // 渲染顺序、计算顺序字节的绝对偏移
        public List<int> RefOffsets = new List<int>();  // 块内 (模型ID, 骨骼) 引用的绝对偏移（外部亲）
        public List<int> RefFrames = new List<int>();   // 与 RefOffsets 对应的关键帧帧号（-1 = 当前状态）

        static readonly Regex CameraWords = new Regex("カメラ|ｶﾒﾗ|camera|cam|追従|追从|追蹤|追踪|鏡頭|镜头|相机|相機", RegexOptions.IgnoreCase);

        /// 看起来像相机骨模型：名称或骨骼名带相机相关字样，且骨骼不多（角色模型通常上百根）。
        public bool LooksLikeCameraRig()
        {
            if (Bones.Count > 120) return false;
            return CameraWords.IsMatch(Name) || Bones.Any(b => CameraWords.IsMatch(b));
        }

        public override string ToString() { return Name; }
    }

    class CameraKey
    {
        public int Frame, FollowOffset, Model, Bone, Fov;
    }

    class PmmFile
    {
        static readonly Encoding Enc = Encoding.GetEncoding(932);
        // 文件路径按系统 ANSI 编码保存（中文系统为 GBK），名称按 Shift-JIS
        static readonly Encoding PathEnc = Encoding.Default;
        public static readonly byte[] Signature = Encoding.ASCII.GetBytes("Polygon Movie maker 0002");

        public string FilePath;
        public byte[] Data;
        public int ModelCountOfs;
        public List<PmmModel> Models = new List<PmmModel>();
        public int CameraStart, CameraEnd;
        public List<CameraKey> CameraKeys = new List<CameraKey>();
        public int RangeSelStart, RangeSelEnd;

        int o;

        public PmmModel ById(int id) { return Models.FirstOrDefault(m => m.Id == id); }

        public string Describe(int id, int bone)
        {
            if (id < 0) return L.T("无", "なし", "none");
            var m = ById(id);
            if (m == null) return L.T("模型#", "モデル#", "model #") + id + " / " + L.T("骨骼#", "ボーン#", "bone #") + bone;
            return m.Name + " / " + (bone >= 0 && bone < m.Bones.Count ? m.Bones[bone] : L.T("骨骼#", "ボーン#", "bone #") + bone);
        }

        public static PmmFile Load(string path)
        {
            var f = new PmmFile { FilePath = path, Data = File.ReadAllBytes(path) };
            f.Parse();
            return f;
        }

        public static PmmFile FromBytes(byte[] data, string path)
        {
            var f = new PmmFile { FilePath = path, Data = data };
            f.Parse();
            return f;
        }

        static string Unsupported { get { return L.T("，可能不是支持的 PMM 版本", "。対応していない PMM バージョンの可能性があります", "; this may not be a supported PMM version"); } }

        // ---------------- 读取 ----------------
        void Need(int n)
        {
            if (n < 0 || o + n > Data.Length)
                throw new PmmException(L.T("文件意外结束（偏移 ", "ファイルが途中で終わっています（オフセット ", "Unexpected end of file (offset ") + o + ")" + Unsupported);
        }
        void Skip(int n) { Need(n); o += n; }
        int U8() { Need(1); return Data[o++]; }
        int I32() { Need(4); int v = BitConverter.ToInt32(Data, o); o += 4; return v; }
        string FixedStr(int n, Encoding enc = null)
        {
            Need(n);
            int end = o;
            while (end < o + n && Data[end] != 0) end++;
            string s = (enc ?? Enc).GetString(Data, o, end - o);
            o += n;
            return s;
        }
        string VarStr() { return FixedStr(U8()); }
        int Count()
        {
            int n = I32();
            if (n < 0 || n > 10000000)
                throw new PmmException(L.T("数量异常 ", "個数が異常です ", "Invalid count ") + n + " (offset " + (o - 4) + ")" + Unsupported);
            return n;
        }
        int KfHeader(bool initial)
        {
            if (!initial) Skip(4);
            int frame = I32();
            Skip(8);
            return frame;
        }

        PmmModel ReadModel()
        {
            var m = new PmmModel { Start = o };
            m.Id = U8();
            m.Name = VarStr();
            VarStr();
            m.PathOfs = o;
            int pe = o; while (pe < o + 256 && pe < Data.Length && Data[pe] != 0) pe++;
            m.PathRaw = Data.Skip(o).Take(pe - o).ToArray();
            m.Path = FixedStr(256, PathEnc);
            U8();
            int nb = Count();
            for (int i = 0; i < nb; i++) m.Bones.Add(VarStr());
            int nmorph = Count();
            for (int i = 0; i < nmorph; i++) VarStr();
            int nik = Count(); Skip(4 * nik);
            int nep = Count(); Skip(4 * nep);
            m.RenderOfs = o;
            Skip(1 + 1 + 4 + 16);
            Skip(U8());
            Skip(4 + 4);
            Skip(nb * (12 + 46));                       // 初始骨骼帧
            Skip(Count() * (16 + 46));                  // 骨骼帧
            Skip(nmorph * (12 + 5));
            Skip(Count() * (16 + 5));
            int modelKf = 1 + nik + 8 * nep + 1;        // 显示 + IK + 外部亲 + 选中
            for (int pass = 0; pass < 2; pass++)
            {
                int n = pass == 0 ? 1 : Count();
                int hdr = pass == 0 ? 12 : 16;
                for (int k = 0; k < n; k++)
                {
                    int frame = BitConverter.ToInt32(Data, o + hdr - 12);
                    int epBase = o + hdr + 1 + nik;
                    for (int j = 0; j < nep; j++) { m.RefOffsets.Add(epBase + 8 * j); m.RefFrames.Add(frame); }
                    Skip(hdr + modelKf);
                }
            }
            Skip(nb * 31);                              // 当前骨骼状态
            Skip(nmorph * 4);
            Skip(nik);
            for (int j = 0; j < nep; j++) { m.RefOffsets.Add(o + 8); m.RefFrames.Add(-1); Skip(16); }   // 当前外部亲
            Skip(1 + 4 + 1);
            m.CalcOfs = o;
            Skip(1);
            m.End = o;
            return m;
        }

        CameraKey ReadCameraKey(bool initial)
        {
            var k = new CameraKey();
            k.Frame = KfHeader(initial);
            k.FollowOffset = o + 28;
            Skip(28);
            k.Model = I32();
            k.Bone = I32();
            Skip(24 + 1);
            k.Fov = I32();
            Skip(1);
            return k;
        }

        void Parse()
        {
            if (Data.Length < 30 || !Data.Take(Signature.Length).SequenceEqual(Signature))
                throw new PmmException(L.T("不是 MMD 9.x 的 PMM 文件（签名不符）", "MMD 9.x の PMM ファイルではありません（シグネチャ不一致）", "Not an MMD 9.x PMM file (signature mismatch)"));
            o = 30;
            Skip(8 + 4 + 4 + 1 + 6 + 1);
            ModelCountOfs = o;
            int nm = U8();
            for (int i = 0; i < nm; i++) Models.Add(ReadModel());

            CameraStart = o;
            CameraKeys.Add(ReadCameraKey(true));
            int nk = Count();
            for (int i = 0; i < nk; i++) CameraKeys.Add(ReadCameraKey(false));
            Skip(12 + 12 + 12 + 1);
            CameraEnd = o;

            KfHeader(true); Skip(25); Skip(Count() * (16 + 25)); Skip(24);     // 灯光
            U8(); I32();
            int nacc = U8();
            Skip(100 * nacc);
            for (int i = 0; i < nacc; i++)                                    // 饰品
            {
                U8(); FixedStr(100); FixedStr(256); U8();
                KfHeader(true); Skip(39);
                Skip(Count() * (16 + 39));
                Skip(38 + 1);
            }
            Skip(12);                       // 时间轴面板
            Skip(4 + 1);                    // 骨骼选择类型、相机追踪类型
            Skip(11);                       // 预览面板
            Skip(257 + 269 + 272);          // 音频、视频、背景图
            Skip(1 + 1 + 1 + 4 + 4 + 4 + 4 + 1 + 1);
            Skip(21); KfHeader(true); Skip(22); Skip(Count() * (16 + 22));   // 重力
            Skip(1 + 4); KfHeader(true); Skip(6); Skip(Count() * (16 + 6));  // 自阴影
            Skip(12 + 1);
            Skip(8 + 64 + 1 + 1 + 1 + 4);
            U8();
            RangeSelStart = o;
            Skip(5 * nm);
            RangeSelEnd = o;
            if (o != Data.Length)
                throw new PmmException(L.T("文件结构与预期不符（解析结束于 ", "ファイル構造が想定と異なります（解析終了位置 ", "Unexpected file structure (parsing ended at ")
                    + o + " / " + Data.Length + ")" + Unsupported);

            var ids = new HashSet<int>(Models.Select(m => m.Id));
            foreach (var k in CameraKeys)
                if (k.Frame < 0 || k.Frame >= 10000000 || k.Fov < 0 || k.Fov > 180 || (k.Model != -1 && !ids.Contains(k.Model)))
                    throw new PmmException(L.T("相机关键帧数据异常", "カメラキーフレームのデータが異常です", "Invalid camera keyframe data") + Unsupported);
            CameraKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }

        public int RangeSelValue(int id)
        {
            for (int p = RangeSelStart; p < RangeSelEnd; p += 5)
                if (Data[p] == id) return BitConverter.ToInt32(Data, p + 1);
            return 3;
        }
    }

    // ================== 追踪批量绑定 ==================
    static class FollowBinder
    {
        public static string OutputPath(string src)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src),
                System.IO.Path.GetFileNameWithoutExtension(src) + L.T("_追踪", "_追従", "_follow") + System.IO.Path.GetExtension(src));
        }

        public static string Process(string src, bool overwriteAll)
        {
            var sb = new StringBuilder();
            var pmm = PmmFile.Load(src);
            byte[] data = pmm.Data;
            var keys = pmm.CameraKeys;
            var bound = keys.Where(k => k.Model >= 0).ToList();
            sb.AppendLine(L.T("模型 {0} 个，相机关键帧 {1} 个，已绑定追踪 {2} 个",
                              "モデル {0} 個、カメラキーフレーム {1} 個、追従設定済み {2} 個",
                              "{0} models, {1} camera keyframes, {2} with bone follow").Replace("{0}", pmm.Models.Count.ToString())
                              .Replace("{1}", keys.Count.ToString()).Replace("{2}", bound.Count.ToString()));
            if (bound.Count == 0)
                throw new PmmException(L.T("没有任何已绑定追踪的相机关键帧。请先在 MMD 里给第一个相机关键帧设置骨骼追踪并保存。",
                                           "ボーン追従が設定されたカメラキーフレームがありません。先に MMD で最初のカメラキーフレームにボーン追従を設定して保存してください。",
                                           "No camera keyframe has bone follow set. In MMD, set bone follow on the first camera keyframe and save first."));

            var first = bound[0];
            int curM = -1, curB = 0, changed = 0, before = 0;
            bool hasCur = false;
            foreach (var k in keys)
            {
                int tm, tb;
                if (overwriteAll) { tm = first.Model; tb = first.Bone; }
                else if (k.Model >= 0) { curM = k.Model; curB = k.Bone; hasCur = true; continue; }
                else if (!hasCur) { before++; continue; }
                else { tm = curM; tb = curB; }
                if (k.Model == tm && k.Bone == tb) continue;
                Array.Copy(BitConverter.GetBytes(tm), 0, data, k.FollowOffset, 4);
                Array.Copy(BitConverter.GetBytes(tb), 0, data, k.FollowOffset + 4, 4);
                changed++;
            }

            string dst = OutputPath(src);
            File.WriteAllBytes(dst, data);
            var check = PmmFile.Load(dst);
            int unbound = check.CameraKeys.Count(k => k.Model < 0);

            sb.AppendLine(L.T("第一个已绑定：第 ", "最初の追従設定：", "First bound key: frame ") + first.Frame + L.T(" 帧 → ", " フレーム → ", " → ") + pmm.Describe(first.Model, first.Bone));
            if (!overwriteAll)
            {
                var targets = bound.Select(k => pmm.Describe(k.Model, k.Bone)).Distinct().ToList();
                if (targets.Count > 1) sb.AppendLine(L.T("中途手动绑定的目标：", "途中で手動設定された追従先：", "Targets set manually later: ") + string.Join(L.T("、", "、", ", "), targets));
            }
            sb.AppendLine(L.T("已绑定 ", "設定したキーフレーム ", "Bound ") + changed + L.T(" 个关键帧", " 個", " keyframes")
                + (before > 0 ? L.T("；第 ", "；", "; ") + before + L.T(" 个在第一个已绑定帧之前，未处理", " 個は最初の設定より前のため未処理", " before the first bound key were left as is") : "")
                + L.T("，处理后仍未绑定 ", "、処理後も未設定 ", ", still unbound: ") + unbound);
            sb.AppendLine(L.T("输出：", "出力：", "Output: ") + dst);
            return sb.ToString();
        }
    }

    // ================== 合并相机骨 ==================
    class RefTarget
    {
        public int Model = -1, Bone = 0;   // 目标 PMM 中的模型 ID / 骨骼序号；Model=-1 表示解除
    }

    enum MatchKind { Unknown, SameModel, ByBoneName, NotFound }

    class RefUser
    {
        public string Model;               // 引用它的模型名；null = 相机追踪
        public List<int> Frames = new List<int>();   // 关键帧帧号（-1 = 当前状态）

        public string Who
        {
            get { return Model == null ? L.T("相机追踪", "カメラ追従", "Camera follow") : Model + L.T(" 的外部亲", " の外部親", " (outside parent)"); }
        }
        public int KeyCount { get { return Frames.Where(f => f >= 0).Distinct().Count(); } }
        public string Range
        {
            get
            {
                var fs = Frames.Where(f => f >= 0).ToList();
                if (fs.Count == 0) return L.T("仅当前状态", "現在の状態のみ", "current state only");
                int a = fs.Min(), b = fs.Max();
                return a == b ? a.ToString() : a + " – " + b;
            }
        }
    }

    class ExternalRef
    {
        public int SrcModel, SrcBone;       // 相机骨 PMM 中被引用的模型 ID / 骨骼
        public string SrcModelName = "", SrcBoneName = "";
        public RefTarget Target = new RefTarget();
        public MatchKind Kind;              // 默认目标的来源
        public int Candidates;
        public List<RefUser> Users = new List<RefUser>();

        public string Match
        {
            get
            {
                switch (Kind)
                {
                    case MatchKind.SameModel: return L.T("同名模型", "同名モデル", "Same model");
                    case MatchKind.ByBoneName:
                        return L.T("按骨骼名匹配，请确认", "ボーン名で一致・要確認", "By bone name — check")
                            + (Candidates > 1 ? L.T("（" + Candidates + " 个候选）", "（候補 " + Candidates + "）", " (" + Candidates + " candidates)") : "");
                    case MatchKind.NotFound: return L.T("未找到同名骨骼", "同名ボーンなし", "No same-name bone");
                    default: return L.T("无法识别", "不明", "Unknown");
                }
            }
        }

        public RefUser User(string model)
        {
            var u = Users.FirstOrDefault(x => x.Model == model);
            if (u == null) { u = new RefUser { Model = model }; Users.Add(u); }
            return u;
        }
    }

    static class Merger
    {
        static int RefModel(byte[] d, int ofs) { return BitConverter.ToInt32(d, ofs); }
        static int RefBone(byte[] d, int ofs) { return BitConverter.ToInt32(d, ofs + 4); }

        /// 列出移入的模型与相机所引用的、不随之移入的外部模型骨骼，并按名字给出默认替换目标。
        public static List<ExternalRef> CollectRefs(PmmFile render, PmmFile rig, ICollection<int> moveIds, bool replaceCamera)
        {
            // 每个不同的（模型, 骨骼）组合单独一行：关键帧中途切换目标时会出现多行，可分别指定
            var dict = new Dictionary<long, ExternalRef>();
            Action<int, int, string, int> add = (m, b, who, frame) =>
            {
                if (m < 0 || moveIds.Contains(m)) return;
                long key = ((long)m << 32) | (uint)b;
                ExternalRef r;
                if (!dict.TryGetValue(key, out r))
                {
                    r = new ExternalRef { SrcModel = m, SrcBone = b };
                    var sm = rig.ById(m);
                    string bone = sm != null && b >= 0 && b < sm.Bones.Count ? sm.Bones[b] : null;
                    r.SrcModelName = sm != null ? sm.Name : "#" + m;
                    r.SrcBoneName = bone ?? "#" + b;
                    DefaultTarget(render, sm, bone, r);
                    dict[key] = r;
                }
                r.User(who).Frames.Add(frame);
            };
            foreach (var m in rig.Models.Where(x => moveIds.Contains(x.Id)))
                for (int i = 0; i < m.RefOffsets.Count; i++)
                    add(RefModel(rig.Data, m.RefOffsets[i]), RefBone(rig.Data, m.RefOffsets[i]), m.Name, m.RefFrames[i]);
            if (replaceCamera)
                foreach (var k in rig.CameraKeys)
                    add(k.Model, k.Bone, null, k.Frame);
            return dict.Values.OrderBy(r => r.SrcModel).ThenBy(r => r.SrcBone).ToList();
        }

        /// 默认替换目标：先找同名模型的同名骨骼；没有同名模型时，在含有同名骨骼的模型里
        /// 选骨骼最多的那个（通常是角色），并标注需要确认。
        static void DefaultTarget(PmmFile render, PmmModel sm, string bone, ExternalRef r)
        {
            if (sm == null || bone == null) { r.Kind = MatchKind.Unknown; return; }
            var same = render.Models.FirstOrDefault(x => x.Name == sm.Name && x.Bones.Contains(bone));
            if (same != null)
            {
                r.Target.Model = same.Id; r.Target.Bone = same.Bones.IndexOf(bone);
                r.Kind = MatchKind.SameModel;
                return;
            }
            var cands = render.Models.Where(x => x.Bones.Contains(bone)).OrderByDescending(x => x.Bones.Count).ToList();
            if (cands.Count > 0)
            {
                r.Target.Model = cands[0].Id; r.Target.Bone = cands[0].Bones.IndexOf(bone);
                r.Kind = MatchKind.ByBoneName; r.Candidates = cands.Count;
                return;
            }
            r.Kind = MatchKind.NotFound;
        }

        /// 移入模型的文件找不到时自动重新定位：按日文编码重读路径；在给定文件夹里搜同名文件。找不到返回 null。
        public static string ResolveModelPath(PmmModel m, IEnumerable<string> searchDirs)
        {
            if (File.Exists(m.Path)) return m.Path;
            try
            {
                string sj = Encoding.GetEncoding(932).GetString(m.PathRaw);
                if (File.Exists(sj)) return sj;
                string name = System.IO.Path.GetFileName(sj);
                foreach (var dir in searchDirs.Where(Directory.Exists).Distinct())
                {
                    try
                    {
                        var hit = Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null) return hit;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return null;
        }

        public static string DefaultOutput(string renderPath)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(renderPath),
                System.IO.Path.GetFileNameWithoutExtension(renderPath) + L.T("_合并", "_マージ", "_merged") + ".pmm");
        }

        public static string Merge(PmmFile render, PmmFile rig, ICollection<int> moveIds, bool replaceCamera,
                                   List<ExternalRef> refs, string dst, IDictionary<int, string> pathOverride = null)
        {
            var sb = new StringBuilder();
            var moving = rig.Models.Where(m => moveIds.Contains(m.Id)).ToList();
            if (moving.Count == 0 && !replaceCamera)
                throw new PmmException(L.T("没有选择要移入的模型，也没有替换相机", "移すモデルもカメラの置き換えも選択されていません", "Nothing to do: no model selected and camera not replaced"));
            if (render.Models.Count + moving.Count > 255)
                throw new PmmException(L.T("模型总数超过 255", "モデル数が 255 を超えます", "More than 255 models in total"));

            // 新模型 ID：接在目标 PMM 现有最大 ID 之后
            var newId = new Dictionary<int, int>();
            int next = render.Models.Count == 0 ? 0 : render.Models.Max(m => m.Id) + 1;
            foreach (var m in moving)
            {
                if (next > 255) throw new PmmException(L.T("模型 ID 超出范围", "モデル ID が範囲外です", "Model ID out of range"));
                newId[m.Id] = next++;
            }
            var refMap = refs.ToDictionary(r => ((long)r.SrcModel << 32) | (uint)r.SrcBone, r => r.Target);

            Func<int, int, int[]> map = (m, b) =>
            {
                if (m < 0) return new[] { -1, 0 };
                int nid;
                if (newId.TryGetValue(m, out nid)) return new[] { nid, b };
                RefTarget t;
                if (refMap.TryGetValue(((long)m << 32) | (uint)b, out t)) return new[] { t.Model, t.Model < 0 ? 0 : t.Bone };
                return new[] { -1, 0 };
            };
            Action<byte[], int> remap = (buf, ofs) =>
            {
                var r = map(BitConverter.ToInt32(buf, ofs), BitConverter.ToInt32(buf, ofs + 4));
                Array.Copy(BitConverter.GetBytes(r[0]), 0, buf, ofs, 4);
                Array.Copy(BitConverter.GetBytes(r[1]), 0, buf, ofs + 4, 4);
            };

            var outp = new MemoryStream();
            byte[] rd = render.Data;
            outp.Write(rd, 0, render.ModelCountOfs);
            outp.WriteByte((byte)(render.Models.Count + moving.Count));
            outp.Write(rd, render.ModelCountOfs + 1, render.CameraStart - render.ModelCountOfs - 1);

            int order = render.Models.Count;
            var newPaths = new List<string>();
            foreach (var m in moving)
            {
                order++;
                var blk = new byte[m.End - m.Start];
                Array.Copy(rig.Data, m.Start, blk, 0, blk.Length);
                blk[0] = (byte)newId[m.Id];
                blk[m.RenderOfs - m.Start] = (byte)order;
                blk[m.CalcOfs - m.Start] = (byte)order;
                foreach (int ofs in m.RefOffsets) remap(blk, ofs - m.Start);
                string newPath = null;
                if (pathOverride != null && pathOverride.TryGetValue(m.Id, out newPath) && newPath != m.Path)
                {
                    // 路径按本机系统编码写入（MMD 按系统编码读取）
                    byte[] pb = Encoding.Default.GetBytes(newPath);
                    if (pb.Length > 255 || Encoding.Default.GetString(pb) != newPath)
                        throw new PmmException(L.T("模型路径过长或含有本机编码无法表示的字符：", "モデルのパスが長すぎるか、このPCの文字コードで表せない文字を含みます：",
                                                   "Model path is too long or has characters the system code page cannot store: ") + newPath);
                    int po = m.PathOfs - m.Start;
                    Array.Clear(blk, po, 256);
                    Array.Copy(pb, 0, blk, po, pb.Length);
                }
                else newPath = m.Path;
                newPaths.Add(newPath);
                outp.Write(blk, 0, blk.Length);
                sb.AppendLine(L.T("移入模型：", "移したモデル：", "Moved model: ") + m.Name + L.T("（ID ", "（ID ", " (ID ") + newId[m.Id] + L.T("，", "、", ", ")
                              + m.Bones.Count + L.T(" 根骨骼）", " ボーン）", " bones)")
                              + (newPath != m.Path ? L.T("  路径改为：", "  パスを変更：", "  path changed to: ") + newPath : "")
                              + (File.Exists(newPath) ? "" : L.T("  ⚠ 模型文件不存在：", "  ⚠ モデルファイルがありません：", "  ⚠ model file not found: ") + newPath));
            }

            if (replaceCamera)
            {
                var cam = new byte[rig.CameraEnd - rig.CameraStart];
                Array.Copy(rig.Data, rig.CameraStart, cam, 0, cam.Length);
                foreach (var k in rig.CameraKeys) remap(cam, k.FollowOffset - rig.CameraStart);
                outp.Write(cam, 0, cam.Length);
                sb.AppendLine(L.T("相机：替换为相机骨 PMM 的相机（关键帧 ", "カメラ：カメラボーン PMM のカメラに置き換え（キーフレーム ", "Camera: replaced with the camera-rig PMM's camera (")
                              + rig.CameraKeys.Count + L.T(" 个）", " 個）", " keyframes)"));
            }
            else
            {
                outp.Write(rd, render.CameraStart, render.CameraEnd - render.CameraStart);
                sb.AppendLine(L.T("相机：保留目标 PMM 原有相机", "カメラ：統合先 PMM のカメラをそのまま使用", "Camera: kept the target PMM's camera"));
            }

            outp.Write(rd, render.CameraEnd, render.RangeSelEnd - render.CameraEnd);
            foreach (var m in moving)
            {
                outp.WriteByte((byte)newId[m.Id]);
                outp.Write(BitConverter.GetBytes(rig.RangeSelValue(m.Id)), 0, 4);
            }
            outp.Write(rd, render.RangeSelEnd, rd.Length - render.RangeSelEnd);

            byte[] result = outp.ToArray();
            var check = PmmFile.FromBytes(result, dst);     // 完整重新解析校验
            if (check.Models.Count != render.Models.Count + moving.Count)
                throw new PmmException(L.T("合并结果校验失败（模型数不符）", "結果の検証に失敗しました（モデル数が一致しません）", "Verification of the result failed (model count mismatch)"));
            File.WriteAllBytes(dst, result);

            sb.Append(Emm.Write(render, newPaths, dst));

            foreach (var r in refs)
                sb.AppendLine(L.T("绑定：", "割り当て：", "Binding: ") + rig.Describe(r.SrcModel, r.SrcBone) + " → "
                              + (r.Target.Model < 0 ? L.T("无（解除）", "なし（解除）", "none (unbind)") : check.Describe(r.Target.Model, r.Target.Bone)));
            int unbound = check.CameraKeys.Count(k => k.Model < 0);
            if (replaceCamera)
                sb.AppendLine(L.T("相机追踪：已绑定 ", "カメラ追従：設定済み ", "Camera follow: bound ") + (check.CameraKeys.Count - unbound)
                              + L.T(" 个，未绑定 ", " 個、未設定 ", ", unbound ") + unbound);
            sb.AppendLine(L.T("输出：", "出力：", "Output: ") + dst);
            return sb.ToString();
        }
    }

    // ================== EMM（MME 特效分配） ==================
    // EMM 用 PmdN / AcsN 给对象编号，N 是 MMD 的整体绘制顺序：模型前绘制的饰品、模型（按渲染顺序）、其余饰品。
    // 移入的模型渲染顺序排在最后，所以它的编号 = 第一个模型的编号 + 原模型数；之后的饰品编号顺延。
    // 新模型在主特效页用 MMD 标准绘制（none），在其他特效页（深度、阴影等离屏渲染）隐藏。
    static class Emm
    {
        static readonly Encoding Raw = Encoding.GetEncoding(28591);   // 逐字节保留（路径按系统编码，不做转换）
        static readonly Regex KeyRe = new Regex(@"^(\s*)(Pmd|Acs)(\d+)(?=\s*[=\[.])");
        static readonly Regex OwnerRe = new Regex(@"^(\s*Owner\s*=\s*)(Pmd|Acs)(\d+)");

        public static string Write(PmmFile render, List<string> newPaths, string dst)
        {
            string src = Path.ChangeExtension(render.FilePath, ".emm");
            string emmDst = Path.ChangeExtension(dst, ".emm");
            if (!File.Exists(src))
                return L.T("EMM：目标 PMM 旁没有同名 .emm，未生成", "EMM：統合先 PMM と同名の .emm がないため作成しません", "EMM: no .emm next to the target PMM, none written") + Environment.NewLine;

            string text = Raw.GetString(File.ReadAllBytes(src));
            string eol = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            int objStart = lines.FindIndex(l => l.Trim() == "[Object]");
            int objEnd = objStart < 0 ? -1 : SectionEnd(lines, objStart);
            var pmd = new List<int>(); int lastPmdLine = -1;
            for (int i = objStart + 1; objStart >= 0 && i < objEnd; i++)
            {
                var m = KeyRe.Match(lines[i]);
                if (m.Success && m.Groups[2].Value == "Pmd") { pmd.Add(int.Parse(m.Groups[3].Value)); lastPmdLine = i; }
            }
            int k = newPaths.Count;
            int first = pmd.Count > 0 ? pmd.Min() : 0;
            int newNum = first + render.Models.Count;
            if (k == 0 || objStart < 0 || pmd.Count == 0 || pmd.Max() >= newNum)
            {
                File.Copy(src, emmDst, true);
                return L.T("EMM：已原样复制目标 PMM 的特效分配 → ", "EMM：統合先 PMM のエフェクト割り当てをそのままコピー → ", "EMM: copied the target PMM's effect assignment as is → ")
                       + Path.GetFileName(emmDst) + (k > 0 ? L.T("（EMM 与 PMM 的模型数对不上，新模型未写入）", "（EMM と PMM のモデル数が合わないため新モデルは未登録）", " (EMM does not match the PMM's models; new models not added)") : "")
                       + Environment.NewLine;
            }

            // 1. 编号 ≥ newNum 的对象顺延 k 位（键名和 Owner 引用）
            Func<Match, string, string> bump = (m, rest) =>
            {
                int n = int.Parse(m.Groups[3].Value);
                return m.Groups[1].Value + m.Groups[2].Value + (n >= newNum ? n + k : n) + rest;
            };
            for (int i = 0; i < lines.Count; i++)
            {
                var m = KeyRe.Match(lines[i]);
                if (m.Success) { lines[i] = bump(m, lines[i].Substring(m.Length)); continue; }
                m = OwnerRe.Match(lines[i]);
                if (m.Success) lines[i] = bump(m, lines[i].Substring(m.Length));
            }

            // 2. 各特效页给新模型加一行（插在编号比它小的最后一行之后）
            for (int s = lines.Count - 1; s >= 0; s--)
            {
                string h = lines[s].Trim();
                if (!h.StartsWith("[Effect")) continue;
                bool main = h == "[Effect]";
                var add = new List<string>();
                for (int j = 0; j < k; j++) add.Add("Pmd" + (newNum + j) + (main ? " = none" : ".show = false"));
                lines.InsertRange(InsertPos(lines, s, newNum), add);
            }
            // 3. [Object] 登记新模型路径
            var obj = new List<string>();
            for (int j = 0; j < k; j++) obj.Add("Pmd" + (newNum + j) + " = " + Raw.GetString(Encoding.Default.GetBytes(newPaths[j])));
            lines.InsertRange(lastPmdLine + 1, obj);

            File.WriteAllBytes(emmDst, Raw.GetBytes(string.Join(eol, lines)));
            return L.T("EMM：已生成 ", "EMM：作成 ", "EMM: wrote ") + Path.GetFileName(emmDst)
                   + L.T("（保留目标 PMM 的全部特效分配；新模型登记为 Pmd", "（統合先 PMM の割り当てをすべて保持。新モデルを Pmd", " (all effect assignments of the target PMM kept; new models registered as Pmd")
                   + newNum + (k > 1 ? "–" + (newNum + k - 1) : "")
                   + L.T("，主特效为 MMD 标准、其他特效页隐藏）", " として登録、メインは MMD 標準・他のタブは非表示）", ", standard MMD shading in the main tab, hidden in other tabs)")
                   + Environment.NewLine;
        }

        static int SectionEnd(List<string> lines, int header)
        {
            int i = header + 1;
            while (i < lines.Count && !lines[i].TrimStart().StartsWith("[")) i++;
            return i;
        }

        static int InsertPos(List<string> lines, int header, int newNum)
        {
            int end = SectionEnd(lines, header), pos = header + 1;
            for (int i = header + 1; i < end; i++)
            {
                var m = KeyRe.Match(lines[i]);
                if (m.Success && int.Parse(m.Groups[3].Value) < newNum) pos = i + 1;
                else if (!m.Success && lines[i].Trim().Length > 0 && pos == i) pos = i + 1;   // Owner/Default 等表头行
            }
            return pos;
        }
    }
}
