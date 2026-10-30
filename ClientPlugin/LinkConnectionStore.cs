using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using VRage.FileSystem;
using VRage.Utils;

namespace Lanyard;

internal sealed class LinkConnectionStore
{
    private static string PathFor(ulong account)
    {
        if (account == 0) throw new ArgumentOutOfRangeException(nameof(account));
        return Path.Combine(MyFileSystem.UserDataPath, "Storage",
            "Lanyard.LinkConnection.v1." + account.ToString(CultureInfo.InvariantCulture) + ".xml");
    }

    public LinkConnectionState Load(ulong account)
    {
        try
        {
            var path = PathFor(account);
            if (!File.Exists(path)) return null;
            var document = new XmlDocument { XmlResolver = null };
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 1024 * 1024) throw new InvalidDataException("Saved link is too large.");
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 524288
                })) document.Load(reader);
            }
            var root = document.DocumentElement;
            if (root == null || root.Name != "LanyardLink" || root.GetAttribute("Version") != "1" ||
                ulong.Parse(root.GetAttribute("Account"), CultureInfo.InvariantCulture) != account)
                throw new InvalidDataException("Saved link header is invalid.");
            if (root.GetAttribute("Unlinked") == "true") return null;
            var state = new LinkConnectionState
            {
                AccountId = account,
                IdentityId = long.Parse(root.GetAttribute("Identity"), CultureInfo.InvariantCulture),
                BlockId = long.Parse(root.GetAttribute("Block"), CultureInfo.InvariantCulture),
                Definition = root.GetAttribute("Definition"),
                OwnerId = long.Parse(root.GetAttribute("Owner"), CultureInfo.InvariantCulture),
                DummyName = root.GetAttribute("Port"),
                OriginWorldId = Guid.Parse(root.GetAttribute("World")),
                Paused = bool.Parse(root.GetAttribute("Paused")),
                Resume = new CombatResumeState
                {
                    GridIds = ReadIds(root, "Grids"), WeaponIds = ReadIds(root, "Weapons")
                }
            };
            Validate(state);
            return state;
        }
        catch (Exception exception)
        {
            MyLog.Default?.WriteLineAndConsole("Lanyard saved link ignored: " + exception.Message);
            return null;
        }
    }

    public void Save(LinkConnectionState state)
    {
        Validate(state);
        var document = new XmlDocument();
        var root = document.CreateElement("LanyardLink");
        document.AppendChild(root);
        Header(root, state.AccountId);
        root.SetAttribute("Identity", state.IdentityId.ToString(CultureInfo.InvariantCulture));
        root.SetAttribute("Block", state.BlockId.ToString(CultureInfo.InvariantCulture));
        root.SetAttribute("Definition", state.Definition);
        root.SetAttribute("Owner", state.OwnerId.ToString(CultureInfo.InvariantCulture));
        root.SetAttribute("Port", state.DummyName);
        root.SetAttribute("World", state.OriginWorldId.ToString("D"));
        root.SetAttribute("Paused", state.Paused.ToString(CultureInfo.InvariantCulture));
        WriteIds(root, "Grids", state.Resume?.GridIds);
        WriteIds(root, "Weapons", state.Resume?.WeaponIds);
        Write(state.AccountId, document);
    }

    public void Forget(ulong account)
    {
        var document = new XmlDocument();
        var root = document.CreateElement("LanyardLink");
        document.AppendChild(root);
        Header(root, account);
        root.SetAttribute("Unlinked", "true");
        Write(account, document);
    }

    private static void Header(XmlElement root, ulong account)
    {
        root.SetAttribute("Version", "1");
        root.SetAttribute("Account", account.ToString(CultureInfo.InvariantCulture));
    }

    private static void Validate(LinkConnectionState state)
    {
        if (state == null || state.AccountId == 0 || state.IdentityId == 0 || state.BlockId == 0 || state.OwnerId == 0 ||
            string.IsNullOrEmpty(state.Definition) || state.Definition.Length > 512 ||
            string.IsNullOrEmpty(state.DummyName) || state.DummyName.Length > 256 ||
            (state.Resume?.GridIds?.Length ?? 0) > CombatResumeState.MaxIds ||
            (state.Resume?.WeaponIds?.Length ?? 0) > CombatResumeState.MaxIds)
            throw new InvalidDataException("Saved link fingerprint is invalid.");
    }

    private static long[] ReadIds(XmlElement root, string name)
    {
        var nodes = root.SelectNodes(name + "/Id");
        if (nodes.Count > CombatResumeState.MaxIds) throw new InvalidDataException("Saved observer list is too large.");
        var ids = new long[nodes.Count];
        for (var index = 0; index < ids.Length; index++)
            if (!long.TryParse(nodes[index].InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ids[index]) || ids[index] == 0)
                throw new InvalidDataException("Saved observer identity is invalid.");
        return ids;
    }

    private static void WriteIds(XmlElement root, string name, long[] ids)
    {
        var list = root.OwnerDocument.CreateElement(name);
        root.AppendChild(list);
        if (ids == null) return;
        foreach (var id in ids)
        {
            if (id == 0) continue;
            var entry = root.OwnerDocument.CreateElement("Id");
            entry.InnerText = id.ToString(CultureInfo.InvariantCulture);
            list.AppendChild(entry);
        }
    }

    private static void Write(ulong account, XmlDocument document)
    {
        var path = PathFor(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
                    document.Save(writer);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}
