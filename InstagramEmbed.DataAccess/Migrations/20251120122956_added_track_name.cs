using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InstagramEmbed.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class added_track_name : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackName",
                table: "Posts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrackName",
                table: "Posts");
        }
    }
}
