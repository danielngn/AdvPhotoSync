using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PhotoMetadata;

namespace LowResPhoto
{
    public class MetaRetriever
    {
        public static Photo RetrieveFromFile(FileInfo file)
        {
            var photo = new Photo() { FullPath = file.FullName, Name = file.Name };
            using (var stream = file.OpenRead())
            {
                var img = Image.FromStream(stream, false, false);
                photo.Width = img.Width;
                photo.Height = img.Height;
                photo.EquipManufacturer = GetStringFromId(0x10f, img);
                photo.EquipModel = GetStringFromId(0x110, img);
                photo.SoftwareUsed = GetStringFromId(0x131, img);
                photo.ISO = GetIntFromId(0x8827, img);
                photo.FocalLength = GetDoubleFromId(0x920a, img);

                var apertureApex = GetDoubleFromId(0x9202, img);
                if (apertureApex != null)
                    photo.FNumber = Math.Round(Math.Pow(2, apertureApex.Value / 2), 1);

                var shutterApex = GetDoubleFromId(0x9201, img);
                if (shutterApex != null)
                    photo.ShutterSpeed = Math.Round(Math.Pow(2, shutterApex.Value), 0);

                var dateTakenStr = GetStringFromId(0x132, img);
                if (!string.IsNullOrEmpty(dateTakenStr))
                {
                    DateTime dateTaken;
                    if (DateTime.TryParseExact(dateTakenStr, "yyyy:MM:d H:m:s", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTaken))
                    {
                        photo.DateTaken = dateTaken;
                    }
                }
            }
            return photo;
        }

        private static Encoding aEncoding = new ASCIIEncoding();

        private static PropertyItem GetItemFromId(int id, Image img)
        {
            return img.PropertyItems.FirstOrDefault(x => x.Id == id);
        }

        private static string GetStringFromId(int id, Image img) //type 2
        {
            var prop = GetItemFromId(id, img);
            if (prop == null)
                return null;

            return aEncoding.GetString(prop.Value, 0, prop.Len - 1);
        }

        private static int? GetIntFromId(int id, Image img) //type 3
        {
            var prop = GetItemFromId(id, img);
            if (prop == null)
                return null;

            return BitConverter.ToInt16(prop.Value, 0);
        }

        private static double? GetDoubleFromId(int id, Image img) //type 5
        {
            var prop = GetItemFromId(id, img);
            if (prop != null)
            {
                double a = BitConverter.ToUInt32(prop.Value, 0);
                double b = BitConverter.ToUInt32(prop.Value, 4);
                return a / b;
            }
            return null;
        }
    }
}
