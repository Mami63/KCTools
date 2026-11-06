using System;
using System.IO;

namespace KCTools.Models
{
    /// <summary>QCN yedek listesindeki bir kayıt.</summary>
    public sealed class QcnBackup
    {
        public QcnBackup(string path)
        {
            Path = path;

            var info = new FileInfo(path);
            Name = System.IO.Path.GetFileName(path);
            Size = info.Exists ? info.Length : 0;
            Modified = info.Exists ? info.LastWriteTime : DateTime.MinValue;
        }

        public string Path { get; }

        public string Name { get; }

        public long Size { get; }

        public DateTime Modified { get; }

        public string Display =>
            Modified.ToString("dd.MM.yyyy HH:mm") + " · " +
            (Size / 1024.0).ToString("N0") + " KB · " + Name;
    }
}
