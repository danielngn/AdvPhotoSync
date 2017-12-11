using System;

namespace PhotoMetadata
{
    public class Photo
    {
        public int PhototId { get; set; }
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTime DateTaken { get; set; }
    }
}
