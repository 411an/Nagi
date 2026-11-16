using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nagi.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCueTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Songs_FilePath",
                table: "Songs");

            migrationBuilder.AddColumn<int>(
                name: "CueTrackNumber",
                table: "Songs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "StartOffsetTicks",
                table: "Songs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CueFilePath",
                table: "Songs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CueFileModifiedDate",
                table: "Songs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_FilePath_CueTrackNumber",
                table: "Songs",
                columns: new[] { "FilePath", "CueTrackNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrading AddCueTracks is not supported because cue tracks cannot be preserved in the single-file schema.");
        }
    }
}
