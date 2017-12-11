using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;

namespace PhotoMetadata
{
    public class PhotoContext : DbContext
    {
        public DbSet<Photo> Photos;
    }
}
