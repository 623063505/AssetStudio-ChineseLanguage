using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using static AssetStudio.ImportHelper;

namespace AssetStudio
{
    public class AssetsManager
    {
        public Game Game;
        public bool Silent = false;
        public bool SkipProcess = false;
        public bool ResolveDependencies = false;
        public string SpecifyUnityVersion;
        public CancellationTokenSource tokenSource = new CancellationTokenSource();
        public List<SerializedFile> assetsFileList = new List<SerializedFile>();

        internal Dictionary<string, int> assetsFileIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, BinaryReader> resourceFileReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);

        internal List<string> importFiles = new List<string>();
        internal HashSet<string> importFilesHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> noexistFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> assetsFileListHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public class AssetFilterDataItem
        {
            public String Source { get; set; }
            public ClassIDType Type { get; set; }
            public String Name { get; set; }
            public long PathID { get; set; }
        }

        public class AssetFilterData
        {
            public List<AssetFilterDataItem> Items { get; set; }
        }

        public AssetFilterData FilterData = new AssetFilterData { Items = new List<AssetFilterDataItem>() };
        public void LoadFiles(params string[] files)
        {
            if (this.Silent)
            {
                Logger.Silent = true;
                Progress.Silent = true;
            }
            ImportHelper.MergeSplitAssets(Path.GetDirectoryName(Path.GetFullPath(files[0])), false);
            string[] toReadFile = ImportHelper.ProcessingSplitFiles(files.ToList<string>());
            if (this.ResolveDependencies)
            {
                toReadFile = AssetsHelper.ProcessDependencies(toReadFile);
            }
            this.Load(toReadFile);
            if (this.Silent)
            {
                Logger.Silent = false;
                Progress.Silent = false;
            }
        }

        public void LoadFolder(string path)
        {
            if (this.Silent)
            {
                Logger.Silent = true;
                Progress.Silent = true;
            }
            ImportHelper.MergeSplitAssets(path, true);
            string[] toReadFile = ImportHelper.ProcessingSplitFiles(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList<string>());
            this.Load(toReadFile);
            if (this.Silent)
            {
                Logger.Silent = false;
                Progress.Silent = false;
            }
        }

        private void Load(string[] files)
        {
            // �����ļ����飬�����ļ�·���������Թ����ظ���
            foreach (var file in files)
            {
                Logger.Verbose($"����{file}·���������Թ��˵��ظ���");
                importFiles.Add(file);
                importFilesHash.Add(Path.GetFileName(file));
            }

            // ���ý���
            Progress.Reset();

            // ʹ�� for ѭ������Ϊ�б��С���ܻ�ı�
            for (var i = 0; i < importFiles.Count; i++)
            {
                // ���ص����ļ�
                LoadFile(importFiles[i]);
                // �������
                Progress.Report(i + 1, importFiles.Count);

                // ����Ƿ�����ȡ������
                if (tokenSource.IsCancellationRequested)
                {
                    Logger.Info("�����ļ�����ֹ!!");
                    break;
                }
            }

            // ��ջ����б�
            importFiles.Clear();
            importFilesHash.Clear();
            noexistFiles.Clear();
            assetsFileListHash.Clear();

            // ����ƫ����
            AssetsHelper.ClearOffsets();

            // ����������ȡ�ʹ����ʲ�
            if (!SkipProcess)
            {
                ReadAssets();
                ProcessAssets();
            }
        }

        private void LoadFile(string fullName)
        {
            var reader = new FileReader(fullName);
            reader = reader.PreProcessing(Game);
            LoadFile(reader);
        }

        private void LoadFile(FileReader reader)
        {
            switch (reader.FileType)
            {
                case FileType.AssetsFile:
                    this.LoadAssetsFile(reader);
                    return;
                case FileType.BundleFile:
                    this.LoadBundleFile(reader, null, 0L, true);
                    return;
                case FileType.WebFile:
                    this.LoadWebFile(reader);
                    return;
                case FileType.ResourceFile:
                case FileType.Blb2File:
                case FileType.Blb3File:
                case FileType.ENCRFile:
                    return;
                case FileType.GZipFile:
                    this.LoadFile(ImportHelper.DecompressGZip(reader));
                    return;
                case FileType.BrotliFile:
                    this.LoadFile(ImportHelper.DecompressBrotli(reader));
                    return;
                case FileType.ZipFile:
                    this.LoadZipFile(reader);
                    return;
                case FileType.BlkFile:
                    this.LoadBlkFile(reader);
                    return;
                case FileType.MhyFile:
                    this.LoadMhyFile(reader, null, 0L, true);
                    return;
                case FileType.BlockFile:
                    this.LoadBlockFile(reader);
                    return;
                default:
                    return;
            }
        }

        private void LoadAssetsFile(FileReader reader)
        {
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                Logger.Info($"������{reader.FullPath}");
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);

                    foreach (var sharedFile in assetsFile.m_Externals)
                    {
                        Logger.Verbose($"{assetsFile.fileName} ��Ҫ�ⲿ�ļ�{sharedFile.fileName},��ͼ������...");
                        var sharedFileName = sharedFile.fileName;

                        if (!importFilesHash.Contains(sharedFileName))
                        {
                            var sharedFilePath = Path.Combine(Path.GetDirectoryName(reader.FullPath), sharedFileName);
                            if (!noexistFiles.Contains(sharedFilePath))
                            {
                                if (!File.Exists(sharedFilePath))
                                {
                                    var findFiles = Directory.GetFiles(Path.GetDirectoryName(reader.FullPath), sharedFileName, SearchOption.AllDirectories);
                                    if (findFiles.Length > 0)
                                    {
                                        Logger.Verbose($"�ҵ�{findFiles.Length}ƥ���ļ�,��ѡ��һ���ļ�{findFiles[0]} !!");
                                        sharedFilePath = findFiles[0];
                                    }
                                }
                                if (File.Exists(sharedFilePath))
                                {
                                    importFiles.Add(sharedFilePath);
                                    importFilesHash.Add(sharedFileName);
                                }
                                else
                                {
                                    Logger.Verbose("ʲôҲû�ҵ������浽�����ڵ��ļ����Ա����ظ�����!!");
                                    noexistFiles.Add(sharedFilePath);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"��ȡ��Դ�ļ�ʱ����{reader.FullPath}", e);
                    reader.Dispose();
                }
            }
            else
            {
                Logger.Info($"����{reader.FullPath}");
                reader.Dispose();
            }
        }


        private void LoadAssetsFromMemory(FileReader reader, string originalPath, string unityVersion = null, long originalOffset = 0)
        {
            Logger.Verbose($"������Դ�ļ�{reader.FileName}��汾{unityVersion}��{originalPath}ƫ����0x{originalOffset:X8}");
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    assetsFile.originalPath = originalPath;
                    assetsFile.offset = originalOffset;
                    if (!string.IsNullOrEmpty(unityVersion) && assetsFile.header.m_Version < SerializedFileFormatVersion.Unknown_7)
                    {
                        assetsFile.SetVersion(unityVersion);
                    }
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);
                }
                catch (Exception e)
                {
                    Logger.Error($"��ȡ��Դ�ļ�ʱ����{reader.FullPath}��{Path.GetFileName(originalPath)}", e);
                    resourceFileReaders.TryAdd(reader.FileName, reader);
                }
            }
            else
                Logger.Info($"����{originalPath} ({reader.FileName})");
        }

        private void LoadBundleFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("������" + reader.FullPath);
            }
            try
            {
                var bundleFile = new BundleFile(reader, Game);
                foreach (var file in bundleFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    if (subReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(subReader, originalPath ?? reader.FullPath, bundleFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("������Դ��");
                        resourceFileReaders.TryAdd(file.fileName, subReader); //TODO
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"��Ϸ���Ͳ�ƥ��,Ԥ��{nameof(Mr0k)}���õ���{Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                var str = $"��ȡ�����ļ�ʱ����{reader.FullPath}";
                if (originalPath != null)
                {
                    str += $"��{Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadWebFile(FileReader reader)
        {
            Logger.Info("������" + reader.FullPath);
            try
            {
                var webFile = new WebFile(reader);
                foreach (var file in webFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    switch (subReader.FileType)
                    {
                        case FileType.AssetsFile:
                            LoadAssetsFromMemory(subReader, reader.FullPath);
                            break;
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath);
                            break;
                        case FileType.WebFile:
                            LoadWebFile(subReader);
                            break;
                        case FileType.ResourceFile:
                            Logger.Verbose("������Դ��");
                            resourceFileReaders.TryAdd(file.fileName, subReader); //TODO
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"��ȡWeb�ļ�ʱ����{reader.FullPath}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadZipFile(FileReader reader)
        {
            Logger.Info("������" + reader.FileName);
            try
            {
                using (ZipArchive archive = new ZipArchive(reader.BaseStream, ZipArchiveMode.Read))
                {
                    List<string> splitFiles = new List<string>();
                    Logger.Verbose("�ڽ����ʲ�֮ǰע�������ļ����Ա�����ҵ��ⲿ���ò��ҵ�����ļ�");
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Contains(".split"))
                        {
                            string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                            string basePath = Path.Combine(Path.GetDirectoryName(entry.FullName), baseName);
                            if (!splitFiles.Contains(basePath))
                            {
                                splitFiles.Add(basePath);
                                importFilesHash.Add(baseName);
                            }
                        }
                        else
                        {
                            importFilesHash.Add(entry.Name);
                        }
                    }

                    Logger.Verbose("�ϲ�����ļ������ؽ��");
                    foreach (string basePath in splitFiles)
                    {
                        try
                        {
                            Stream splitStream = new MemoryStream();
                            int i = 0;
                            while (true)
                            {
                                string path = $"{basePath}.split{i++}";
                                ZipArchiveEntry entry = archive.GetEntry(path);
                                if (entry == null)
                                    break;
                                using (Stream entryStream = entry.Open())
                                {
                                    entryStream.CopyTo(splitStream);
                                }
                            }
                            splitStream.Seek(0, SeekOrigin.Begin);
                            FileReader entryReader = new FileReader(basePath, splitStream);
                            entryReader = entryReader.PreProcessing(Game);
                            LoadFile(entryReader);
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"��ȡzip����ļ�ʱ����{basePath}", e);
                        }
                    }

                    Logger.Verbose("����������Ŀ");
                    Logger.Verbose($"�ҵ�{archive.Entries.Count}��Ŀ");
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        try
                        {
                            string dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), reader.FileName, entry.FullName);
                            Logger.Verbose("����һ���������洢�������������������Թ��Ժ���ȡ");
                            Stream streamReader = new MemoryStream();
                            using (Stream entryStream = entry.Open())
                            {
                                entryStream.CopyTo(streamReader);
                            }
                            streamReader.Position = 0;

                            FileReader entryReader = new FileReader(dummyPath, streamReader);
                            entryReader = entryReader.PreProcessing(Game);
                            LoadFile(entryReader);
                            if (entryReader.FileType == FileType.ResourceFile)
                            {
                                entryReader.Position = 0;
                                Logger.Verbose("������Դ��");
                                resourceFileReaders.TryAdd(entry.Name, entryReader);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"��ȡzip��Ŀʱ����{entry.FullName}", e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"��ȡzip��Ŀʱ����{reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadBlockFile(FileReader reader)
        {
            Logger.Info("������" + reader.FullPath);
            try
            {
                using var stream = new OffsetStream(reader.BaseStream, 0);
                foreach (var offset in stream.GetOffsets(reader.FullPath))
                {
                    var name = offset.ToString("X8");
                    Logger.Info($"���ؿ�{name}");

                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), name);
                    var subReader = new FileReader(dummyPath, stream, true);
                    switch (subReader.FileType)
                    {
                        case FileType.ENCRFile:
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.Blb3File:
                            LoadBlbFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.MhyFile:
                            LoadMhyFile(subReader, reader.FullPath, offset, false);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"��ȡ���ļ�ʱ����{reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadBlkFile(FileReader reader)
        {
            Logger.Info("������" + reader.FullPath);
            try
            {
                using var stream = BlkUtils.Decrypt(reader, (Blk)Game);
                foreach (var offset in stream.GetOffsets(reader.FullPath))
                {
                    var name = offset.ToString("X8");
                    Logger.Info($"���ؿ�{name}");

                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), name);
                    var subReader = new FileReader(dummyPath, stream, true);
                    switch (subReader.FileType)
                    {
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.MhyFile:
                            LoadMhyFile(subReader, reader.FullPath, offset, false);
                            break;
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"��Ϸ���Ͳ�ƥ��,Ԥ��{nameof(Blk)} ���õ���{Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                Logger.Error($"��ȡblk�ļ�ʱ����{reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadMhyFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("������" + reader.FullPath);
            }
            try
            {
                var mhyFile = new MhyFile(reader, (Mhy)Game);
                Logger.Verbose($"�׹����ļ��ܴ�С:{mhyFile.m_Header.size:X8}");
                foreach (var file in mhyFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var cabReader = new FileReader(dummyPath, file.stream);
                    if (cabReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(cabReader, originalPath ?? reader.FullPath, mhyFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("������Դ��");
                        resourceFileReaders.TryAdd(file.fileName, cabReader); //TODO
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"��Ϸ���Ͳ�ƥ��,Ԥ��{nameof(Mhy)}���õ���{Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                var str = $"��ȡ�׹����ļ�ʱ����{reader.FullPath}";
                if (originalPath != null)
                {
                    str += $"��{Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadBlbFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("���ڼ���" + reader.FullPath);
            }
            try
            {
                var blbFile = new Blb3File(reader, reader.FullPath);
                foreach (var file in blbFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var cabReader = new FileReader(dummyPath, file.stream);
                    if (cabReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(cabReader, originalPath ?? reader.FullPath, blbFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("������Դ��");
                        resourceFileReaders.TryAdd(file.fileName, cabReader); //TODO
                    }
                }
            }
            catch (Exception e)
            {
                var str = $"��ȡBlb�ļ�ʱ����{reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public void CheckStrippedVersion(SerializedFile assetsFile)
        {
            if (assetsFile.IsVersionStripped && string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                throw new Exception("Unity�汾�ѱ�����,����ѡ�������ð汾");
            }
            if (!string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                assetsFile.SetVersion(SpecifyUnityVersion);
            }
        }

        public void Clear()
        {
            Logger.Verbose("���...");

            foreach (var assetsFile in assetsFileList)
            {
                assetsFile.Objects.Clear();
                assetsFile.reader.Close();
            }
            assetsFileList.Clear();

            foreach (var resourceFileReader in resourceFileReaders)
            {
                resourceFileReader.Value.Close();
            }
            resourceFileReaders.Clear();

            assetsFileIndexCache.Clear();

            tokenSource.Dispose();
            tokenSource = new CancellationTokenSource();

            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void ReadAssets()
        {
            Logger.Info("��ȡ��Դ...");

            var progressCount = assetsFileList.Sum(x => x.m_Objects.Count);
            int i = 0;
            Progress.Reset();
            foreach (var assetsFile in assetsFileList)
            {
                foreach (var objectInfo in assetsFile.m_Objects)
                {
                    if (tokenSource.IsCancellationRequested)
                    {
                        Logger.Info("��ȡ��Դ�ѱ�ȡ��!!");
                        return;
                    }
                    var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo, Game);
                    try
                    {
                        Object obj = objectReader.type switch
                        {
                            ClassIDType.Animation when ClassIDType.Animation.CanParse() => new Animation(objectReader),
                            ClassIDType.AnimationClip when ClassIDType.AnimationClip.CanParse() => new AnimationClip(objectReader),
                            ClassIDType.Animator when ClassIDType.Animator.CanParse() => new Animator(objectReader),
                            ClassIDType.AnimatorController when ClassIDType.AnimatorController.CanParse() => new AnimatorController(objectReader),
                            ClassIDType.AnimatorOverrideController when ClassIDType.AnimatorOverrideController.CanParse() => new AnimatorOverrideController(objectReader),
                            ClassIDType.AssetBundle when ClassIDType.AssetBundle.CanParse() => new AssetBundle(objectReader),
                            ClassIDType.AudioClip when ClassIDType.AudioClip.CanParse() => new AudioClip(objectReader),
                            ClassIDType.Avatar when ClassIDType.Avatar.CanParse() => new Avatar(objectReader),
                            ClassIDType.Font when ClassIDType.Font.CanParse() => new Font(objectReader),
                            ClassIDType.GameObject when ClassIDType.GameObject.CanParse() => new GameObject(objectReader),
                            ClassIDType.IndexObject when ClassIDType.IndexObject.CanParse() => new IndexObject(objectReader),
                            ClassIDType.Material when ClassIDType.Material.CanParse() => new Material(objectReader),
                            ClassIDType.Mesh when ClassIDType.Mesh.CanParse() => new Mesh(objectReader),
                            ClassIDType.MeshFilter when ClassIDType.MeshFilter.CanParse() => new MeshFilter(objectReader),
                            ClassIDType.MeshRenderer when ClassIDType.MeshRenderer.CanParse() => new MeshRenderer(objectReader),
                            ClassIDType.MiHoYoBinData when ClassIDType.MiHoYoBinData.CanParse() => new MiHoYoBinData(objectReader),
                            ClassIDType.MonoBehaviour when ClassIDType.MonoBehaviour.CanParse() => new MonoBehaviour(objectReader),
                            ClassIDType.MonoScript when ClassIDType.MonoScript.CanParse() => new MonoScript(objectReader),
                            ClassIDType.MovieTexture when ClassIDType.MovieTexture.CanParse() => new MovieTexture(objectReader),
                            ClassIDType.PlayerSettings when ClassIDType.PlayerSettings.CanParse() => new PlayerSettings(objectReader),
                            ClassIDType.RectTransform when ClassIDType.RectTransform.CanParse() => new RectTransform(objectReader),
                            ClassIDType.Shader when ClassIDType.Shader.CanParse() => new Shader(objectReader),
                            ClassIDType.SkinnedMeshRenderer when ClassIDType.SkinnedMeshRenderer.CanParse() => new SkinnedMeshRenderer(objectReader),
                            ClassIDType.Sprite when ClassIDType.Sprite.CanParse() => new Sprite(objectReader),
                            ClassIDType.SpriteAtlas when ClassIDType.SpriteAtlas.CanParse() => new SpriteAtlas(objectReader),
                            ClassIDType.TextAsset when ClassIDType.TextAsset.CanParse() => new TextAsset(objectReader),
                            ClassIDType.Texture2D when ClassIDType.Texture2D.CanParse() => new Texture2D(objectReader),
                            ClassIDType.Transform when ClassIDType.Transform.CanParse() => new Transform(objectReader),
                            ClassIDType.VideoClip when ClassIDType.VideoClip.CanParse() => new VideoClip(objectReader),
                            ClassIDType.ResourceManager when ClassIDType.ResourceManager.CanParse() => new ResourceManager(objectReader),
                            _ => new Object(objectReader),
                        };
                        assetsFile.AddObject(obj);
                    }
                    catch (Exception e)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("�޷����ض���")
                            .AppendLine($"��Դ{assetsFile.fileName}")
                            .AppendLine($"·��{assetsFile.originalPath}")
                            .AppendLine($"����{objectReader.type}")
                            .AppendLine($"·��ID {objectInfo.m_PathID}")
                            .Append(e);
                        Logger.Error(sb.ToString());
                    }

                    Progress.Report(++i, progressCount);
                }
            }
        }

        private void ProcessAssets()
        {
            Logger.Info("������Դ...");

            foreach (var assetsFile in assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (tokenSource.IsCancellationRequested)
                    {
                        Logger.Info("������Դ�ѱ�ȡ��!!");
                        return;
                    }
                    if (obj is GameObject m_GameObject)
                    {
                        Logger.Verbose($"GameObject with {m_GameObject.m_PathID} in file {m_GameObject.assetsFile.fileName} has {m_GameObject.m_Components.Count} components,��ͼȥȡ����...");
                        foreach (var pptr in m_GameObject.m_Components)
                        {
                            if (pptr.TryGet(out var m_Component))
                            {
                                switch (m_Component)
                                {
                                    case Transform m_Transform:
                                        Logger.Verbose($"���ļ����ȡ�任���{m_Transform.m_PathID}{m_Transform.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_Transform = m_Transform;
                                        break;
                                    case MeshRenderer m_MeshRenderer:
                                        Logger.Verbose($"���ļ����ȡ������Ⱦ���{m_MeshRenderer.m_PathID}{m_MeshRenderer.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_MeshRenderer = m_MeshRenderer;
                                        break;
                                    case MeshFilter m_MeshFilter:
                                        Logger.Verbose($"���ļ����ȡ������������{m_MeshFilter.m_PathID}{m_MeshFilter.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_MeshFilter = m_MeshFilter;
                                        break;
                                    case SkinnedMeshRenderer m_SkinnedMeshRenderer:
                                        Logger.Verbose($"���ļ����ȡ��Ƥ������Ⱦ�����{m_SkinnedMeshRenderer.m_PathID}{m_SkinnedMeshRenderer.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_SkinnedMeshRenderer = m_SkinnedMeshRenderer;
                                        break;
                                    case Animator m_Animator:
                                        Logger.Verbose($"���ļ����ȡ����ʦ���{m_Animator.m_PathID}{m_Animator.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_Animator = m_Animator;
                                        break;
                                    case Animation m_Animation:
                                        Logger.Verbose($"���ļ����ȡ�������{m_Animation.m_PathID}{m_Animation.assetsFile.fileName},�����GameObject���...");
                                        m_GameObject.m_Animation = m_Animation;
                                        break;
                                }
                            }
                        }
                    }
                    else if (obj is SpriteAtlas m_SpriteAtlas)
                    {
                        if (m_SpriteAtlas.m_RenderDataMap.Count > 0)
                        {
                            Logger.Verbose($"SpriteAtlas with {m_SpriteAtlas.m_PathID} in file {m_SpriteAtlas.assetsFile.fileName} has {m_SpriteAtlas.m_PackedSprites.Count} packed sprites,��ͼȥȡ����...");
                            foreach (var m_PackedSprite in m_SpriteAtlas.m_PackedSprites)
                            {
                                if (m_PackedSprite.TryGet(out var m_Sprite))
                                {
                                    if (m_Sprite.m_SpriteAtlas.IsNull)
                                    {
                                        Logger.Verbose($"Fetched Sprite with {m_Sprite.m_PathID} in file {m_Sprite.assetsFile.fileName},�����������ͼ��...");
                                        m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                    }
                                    else
                                    {
                                        m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlaOld);
                                        if (m_SpriteAtlaOld.m_IsVariant)
                                        {
                                            Logger.Verbose($"Fetched Sprite with {m_Sprite.m_PathID} in file {m_Sprite.assetsFile.fileName}��һ��ԭʼ�ľ���ͼ������,������岢������������ͼ��...");
                                            m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
