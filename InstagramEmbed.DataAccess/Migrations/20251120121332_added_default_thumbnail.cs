using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InstagramEmbed.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class added_default_thumbnail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultThumbnailUrl",
                table: "Posts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultThumbnailUrl",
                table: "Posts");
        }
    }
}
