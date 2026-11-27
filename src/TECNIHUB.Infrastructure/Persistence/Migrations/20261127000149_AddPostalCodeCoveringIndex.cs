using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TECNIHUB.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostalCodeCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_postal_codes_postal_code",
                table: "postal_codes");

            migrationBuilder.CreateIndex(
                name: "ix_postal_codes_postal_code_neighborhood",
                table: "postal_codes",
                columns: new[] { "postal_code", "neighborhood" })
                .Annotation("Npgsql:IndexInclude", new[] { "state", "municipality", "city" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_postal_codes_postal_code_neighborhood",
                table: "postal_codes");

            migrationBuilder.CreateIndex(
                name: "IX_postal_codes_postal_code",
                table: "postal_codes",
                column: "postal_code");
        }
    }
}
