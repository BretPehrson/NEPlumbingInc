using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NEPlumbingInc.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceImageBlobStorageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceImageBlobName",
                table: "Services",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceImageContentType",
                table: "Services",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServiceImageBlobName",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "ServiceImageContentType",
                table: "Services");
        }
    }
}
