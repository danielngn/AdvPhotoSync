using System;
using System.Linq;
using System.Reflection;

namespace PhotoMetadata
{
    public class Photo
    {
        public int PhotoId { get; set; }
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTime? DateTaken { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int LongSize => Math.Max(Width, Height);
        public string EquipManufacturer { get; set; }
        public string EquipModel { get; set; }
        public string SoftwareUsed { get; set; }
        public double? FocalLength { get; set; }
        public double? FNumber { get; set; }
        public int? ISO { get; set; }
        public double? ShutterSpeed { get; set; }

        private static PropertyInfo[] AllProps { get; } = typeof(Photo).GetProperties();

        public void UpdateFrom(Photo source)
        {
            string[] excludeProps = new string[] { nameof(FullPath), nameof(PhotoId) };
            foreach (var prop in AllProps)
            {
                if (!prop.CanWrite || excludeProps.Contains(prop.Name))
                    continue;
                prop.SetValue(this, prop.GetValue(source));
            }
        }
    }
}
