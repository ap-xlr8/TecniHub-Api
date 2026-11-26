using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TECNIHUB.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicProfilesAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_region",
                table: "users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_reviews_repair_requests_repair_request_id",
                        column: x => x.repair_request_id,
                        principalTable: "repair_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_reviews_users_client_user_id",
                        column: x => x.client_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_reviews_users_technician_user_id",
                        column: x => x.technician_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_reviews_client_user_id",
                table: "service_reviews",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_reviews_repair_request_id",
                table: "service_reviews",
                column: "repair_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_reviews_technician_user_id_created_at",
                table: "service_reviews",
                columns: new[] { "technician_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_reviews");

            migrationBuilder.DropColumn(
                name: "public_region",
                table: "users");
        }
    }
}
