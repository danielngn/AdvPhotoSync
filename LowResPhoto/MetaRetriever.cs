using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using PhotoMetadata;

namespace LowResPhoto
{
    public class MetaRetriever
    {
        public static Photo RetrieveFromFile(FileInfo file)
        {
            var photo = new Photo() { FullPath = file.FullName, Name = file.Name };
            var encoding = new System.Text.ASCIIEncoding();
            using (var stream = file.OpenRead())
            {
                var img = Image.FromStream(stream, false, false);
                var s = encoding.GetString(img.PropertyItems[0].Value);
            }
            return photo;
        }
    }
}
