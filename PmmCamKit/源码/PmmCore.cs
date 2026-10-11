// PMM（MMD 9.x 工程文件，"Polygon Movie maker 0002"）解析与编辑核心。
// 格式参考 Scallion（MIT，paralleltree/Scallion）。
// 注意：外部亲/相机追从/饰品绑定里的"模型序号"是模型自带的 ID 字段，不是列表位置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CatToolbox
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
            if (id < 0) return "无";
            var m = ById(id);
            if (m == null) return "模型#" + id + " / 骨骼#" + bone;
            return m.Name + " / " + (bone >= 0 && bone < m.Bones.Count ? m.Bones[bone] : "骨骼#" + bone);
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

        // ---------------- 读取 ----------------
        void Need(int n)
        {
            if (n < 0 || o + n > Data.Length)
                throw new PmmException("文件意外结束（偏移 " + o + "），可能不是支持的 PMM 版本");
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
                throw new PmmException("数量异常 " + n + "（偏移 " + (o - 4) + "），可能不是支持的 PMM 版本");
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
                    int epBase = o + hdr + 1 + nik;
                    for (int j = 0; j < nep; j++) m.RefOffsets.Add(epBase + 8 * j);
                    Skip(hdr + modelKf);
                }
            }
            Skip(nb * 31);                              // 当前骨骼状态
            Skip(nmorph * 4);
            Skip(nik);
            for (int j = 0; j < nep; j++) { m.RefOffsets.Add(o + 8); Skip(16); }   // 当前外部亲
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
                throw new PmmException("不是 MMD 9.x 的 PMM 文件（签名不符）");
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
            Skip(4 + 1);                    // 骨骼选择类型、相机追从类型
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
                throw new PmmException("文件结构与预期不符（解析结束于 " + o + "，文件长 " + Data.Length + "），可能不是支持的 PMM 版本");

            var ids = new HashSet<int>(Models.Select(m => m.Id));
            foreach (var k in CameraKeys)
                if (k.Frame < 0 || k.Frame >= 10000000 || k.Fov < 0 || k.Fov > 180 || (k.Model != -1 && !ids.Contains(k.Model)))
                    throw new PmmException("相机关键帧数据异常，可能不是支持的 PMM 版本");
            CameraKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }

        public int RangeSelValue(int id)
        {
            for (int p = RangeSelStart; p < RangeSelEnd; p += 5)
                if (Data[p] == id) return BitConverter.ToInt32(Data, p + 1);
            return 3;
        }
    }

    // ================== 追从批量绑定 ==================
    static class FollowBinder
    {
        public static string Process(string src, bool overwriteAll)
        {
            var sb = new StringBuilder();
            var pmm = PmmFile.Load(src);
            byte[] data = pmm.Data;
            var keys = pmm.CameraKeys;
            var bound = keys.Where(k => k.Model >= 0).ToList();
            sb.AppendLine("模型 " + pmm.Models.Count + " 个，相机关键帧 " + keys.Count + " 个，已绑定追从 " + bound.Count + " 个");
            if (bound.Count == 0)
                throw new PmmException("没有任何已绑定追从的相机关键帧。请先在 MMD 里给第一个相机关键帧设置骨骼追从并保存。");

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

            string dst = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src),
                System.IO.Path.GetFileNameWithoutExtension(src) + "_追従" + System.IO.Path.GetExtension(src));
            File.WriteAllBytes(dst, data);
            var check = PmmFile.Load(dst);
            int unbound = check.CameraKeys.Count(k => k.Model < 0);

            sb.AppendLine("第一个已绑定：第 " + first.Frame + " 帧 → " + pmm.Describe(first.Model, first.Bone));
            if (!overwriteAll)
            {
                var targets = bound.Select(k => pmm.Describe(k.Model, k.Bone)).Distinct().ToList();
                if (targets.Count > 1) sb.AppendLine("中途手动绑定的目标：" + string.Join("、", targets));
            }
            sb.AppendLine("已绑定 " + changed + " 个关键帧"
                + (before > 0 ? "；第 " + first.Frame + " 帧之前有 " + before + " 个未处理" : "")
                + "，处理后仍未绑定 " + unbound + " 个");
            sb.AppendLine("输出：" + dst);
            return sb.ToString();
        }
    }

    // ================== 合并相机骨 ==================
    class RefTarget
    {
        public int Model = -1, Bone = 0;   // 目标 PMM 中的模型 ID / 骨骼序号；Model=-1 表示解除
    }

    class ExternalRef
    {
        public int SrcModel, SrcBone;       // 相机骨 PMM 中被引用的模型 ID / 骨骼
        public string UsedBy;               // 谁引用了它（说明用）
        public RefTarget Target = new RefTarget();
    }

    static class Merger
    {
        static int RefModel(byte[] d, int ofs) { return BitConverter.ToInt32(d, ofs); }
        static int RefBone(byte[] d, int ofs) { return BitConverter.ToInt32(d, ofs + 4); }

        /// 列出移入的模型与相机所引用的、不随之移入的外部模型骨骼，并按名字给出默认替换目标。
        public static List<ExternalRef> CollectRefs(PmmFile render, PmmFile rig, ICollection<int> moveIds, bool replaceCamera)
        {
            var dict = new Dictionary<long, ExternalRef>();
            Action<int, int, string> add = (m, b, who) =>
            {
                if (m < 0 || moveIds.Contains(m)) return;
                long key = ((long)m << 32) | (uint)b;
                ExternalRef r;
                if (!dict.TryGetValue(key, out r))
                {
                    r = new ExternalRef { SrcModel = m, SrcBone = b, UsedBy = who };
                    var sm = rig.ById(m);
                    if (sm != null)
                    {
                        var tm = render.Models.FirstOrDefault(x => x.Name == sm.Name);
                        string bone = b >= 0 && b < sm.Bones.Count ? sm.Bones[b] : null;
                        if (tm != null && bone != null && tm.Bones.Contains(bone))
                        {
                            r.Target.Model = tm.Id;
                            r.Target.Bone = tm.Bones.IndexOf(bone);
                        }
                    }
                    dict[key] = r;
                }
                else if (!r.UsedBy.Contains(who)) r.UsedBy += "、" + who;
            };
            foreach (var m in rig.Models.Where(x => moveIds.Contains(x.Id)))
                foreach (int ofs in m.RefOffsets)
                    add(RefModel(rig.Data, ofs), RefBone(rig.Data, ofs), m.Name + " 的外部亲");
            if (replaceCamera)
                foreach (var k in rig.CameraKeys)
                    add(k.Model, k.Bone, "相机追从");
            return dict.Values.OrderBy(r => r.SrcModel).ThenBy(r => r.SrcBone).ToList();
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

        public static string Merge(PmmFile render, PmmFile rig, ICollection<int> moveIds, bool replaceCamera,
                                   List<ExternalRef> refs, string dst, IDictionary<int, string> pathOverride = null)
        {
            var sb = new StringBuilder();
            var moving = rig.Models.Where(m => moveIds.Contains(m.Id)).ToList();
            if (moving.Count == 0 && !replaceCamera) throw new PmmException("没有选择要移入的模型，也没有替换相机");
            if (render.Models.Count + moving.Count > 255) throw new PmmException("模型总数超过 255");

            // 新模型 ID：接在渲染 PMM 现有最大 ID 之后
            var newId = new Dictionary<int, int>();
            int next = render.Models.Count == 0 ? 0 : render.Models.Max(m => m.Id) + 1;
            foreach (var m in moving)
            {
                if (next > 255) throw new PmmException("模型 ID 超出范围");
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
                        throw new PmmException("模型路径过长或含有本机编码无法表示的字符：" + newPath);
                    int po = m.PathOfs - m.Start;
                    Array.Clear(blk, po, 256);
                    Array.Copy(pb, 0, blk, po, pb.Length);
                }
                else newPath = m.Path;
                outp.Write(blk, 0, blk.Length);
                sb.AppendLine("移入模型：" + m.Name + "（ID " + newId[m.Id] + "，" + m.Bones.Count + " 根骨骼）"
                              + (newPath != m.Path ? "  路径改为：" + newPath : "")
                              + (File.Exists(newPath) ? "" : "  ⚠ 模型文件不存在：" + newPath));
            }

            if (replaceCamera)
            {
                var cam = new byte[rig.CameraEnd - rig.CameraStart];
                Array.Copy(rig.Data, rig.CameraStart, cam, 0, cam.Length);
                foreach (var k in rig.CameraKeys) remap(cam, k.FollowOffset - rig.CameraStart);
                outp.Write(cam, 0, cam.Length);
                sb.AppendLine("相机：替换为相机骨 PMM 的相机（关键帧 " + rig.CameraKeys.Count + " 个）");
            }
            else
            {
                outp.Write(rd, render.CameraStart, render.CameraEnd - render.CameraStart);
                sb.AppendLine("相机：保留渲染 PMM 原有相机");
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
                throw new PmmException("合并结果校验失败（模型数不符）");
            File.WriteAllBytes(dst, result);

            foreach (var r in refs)
                sb.AppendLine("绑定：" + rig.Describe(r.SrcModel, r.SrcBone) + " → "
                              + (r.Target.Model < 0 ? "无（解除）" : check.Describe(r.Target.Model, r.Target.Bone)));
            int unbound = check.CameraKeys.Count(k => k.Model < 0);
            if (replaceCamera)
                sb.AppendLine("相机关键帧追从：已绑定 " + (check.CameraKeys.Count - unbound) + " 个，未绑定 " + unbound + " 个");
            sb.AppendLine("输出：" + dst);
            return sb.ToString();
        }
    }
}
