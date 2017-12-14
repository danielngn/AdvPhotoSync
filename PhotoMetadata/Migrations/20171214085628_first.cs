using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace PhotoMetadata.Migrations
{
    public partial class first : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    PhotoId = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DateTaken = table.Column<DateTime>(nullable: true),
                    EquipManufacturer = table.Column<string>(nullable: true),
                    EquipModel = table.Column<string>(nullable: true),
                    FNumber = table.Column<double>(nullable: true),
                    FocalLength = table.Column<double>(nullable: true),
                    FullPath = table.Column<string>(nullable: true),
                    Height = table.Column<int>(nullable: false),
                    ISO = table.Column<int>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    ShutterSpeed = table.Column<double>(nullable: true),
                    SoftwareUsed = table.Column<string>(nullable: true),
                    Width = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.PhotoId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Photos");
        }
    }
}
