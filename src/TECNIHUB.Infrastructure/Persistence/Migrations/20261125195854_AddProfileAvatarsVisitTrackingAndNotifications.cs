using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TECNIHUB.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileAvatarsVisitTrackingAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_conversations_repair_request_id",
                table: "chat_conversations");

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arrival_pin_code",
                table: "repair_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "arrival_pin_failed_attempts",
                table: "repair_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arrived_at",
                table: "repair_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "repair_requests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closure_rejection_reason",
                table: "repair_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "en_route_at",
                table: "repair_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "estimated_arrival_minutes",
                table: "repair_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "chat_messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    auth = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    body = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    action_url = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_repair_request_id_participant_b_user_id",
                table: "chat_conversations",
                columns: new[] { "repair_request_id", "participant_b_user_id" },
                unique: true,
                filter: "repair_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_user_id_is_read_created_at",
                table: "user_notifications",
                columns: new[] { "user_id", "is_read", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "user_notifications");

            migrationBuilder.DropIndex(
                name: "IX_chat_conversations_repair_request_id_participant_b_user_id",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "users");

            migrationBuilder.DropColumn(
                name: "arrival_pin_code",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "arrival_pin_failed_attempts",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "arrived_at",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "closure_rejection_reason",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "en_route_at",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "estimated_arrival_minutes",
                table: "repair_requests");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "chat_messages");

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_repair_request_id",
                table: "chat_conversations",
                column: "repair_request_id",
                unique: true,
                filter: "repair_request_id IS NOT NULL");
        }
    }
}
