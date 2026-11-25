using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TECNIHUB.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreOperationsAndChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "postal_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    postal_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    neighborhood = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    settlement_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_postal_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repair_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appliance_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    brand_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    issue_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    preferred_date = table.Column<DateOnly>(type: "date", nullable: false),
                    preferred_time_slot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    neighborhood = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    exterior_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    interior_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address_reference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closure_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closure_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_requests_users_assigned_technician_id",
                        column: x => x.assigned_technician_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_repair_requests_users_client_user_id",
                        column: x => x.client_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spare_parts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    appliance_category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    compatibility = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    condition = table.Column<int>(type: "integer", nullable: false),
                    modality = table.Column<int>(type: "integer", nullable: false),
                    price_mxn = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spare_parts", x => x.id);
                    table.ForeignKey(
                        name: "FK_spare_parts_users_technician_user_id",
                        column: x => x.technician_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repair_request_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_request_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_request_images_repair_requests_repair_request_id",
                        column: x => x.repair_request_id,
                        principalTable: "repair_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_type = table.Column<int>(type: "integer", nullable: false),
                    repair_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    spare_part_id = table.Column<Guid>(type: "uuid", nullable: true),
                    participant_a_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_b_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_conversations", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_conversations_repair_requests_repair_request_id",
                        column: x => x.repair_request_id,
                        principalTable: "repair_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chat_conversations_spare_parts_spare_part_id",
                        column: x => x.spare_part_id,
                        principalTable: "spare_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chat_conversations_users_participant_a_user_id",
                        column: x => x.participant_a_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chat_conversations_users_participant_b_user_id",
                        column: x => x.participant_b_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spare_part_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spare_part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spare_part_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_spare_part_images_spare_parts_spare_part_id",
                        column: x => x.spare_part_id,
                        principalTable: "spare_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "chat_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_messages_users_sender_user_id",
                        column: x => x.sender_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_participant_a_user_id_last_message_at",
                table: "chat_conversations",
                columns: new[] { "participant_a_user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_participant_b_user_id_last_message_at",
                table: "chat_conversations",
                columns: new[] { "participant_b_user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_repair_request_id",
                table: "chat_conversations",
                column: "repair_request_id",
                unique: true,
                filter: "repair_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_spare_part_id_participant_b_user_id",
                table: "chat_conversations",
                columns: new[] { "spare_part_id", "participant_b_user_id" },
                unique: true,
                filter: "spare_part_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_conversation_id_sender_user_id_read_at",
                table: "chat_messages",
                columns: new[] { "conversation_id", "sender_user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_conversation_id_sent_at",
                table: "chat_messages",
                columns: new[] { "conversation_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_sender_user_id",
                table: "chat_messages",
                column: "sender_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_postal_codes_postal_code",
                table: "postal_codes",
                column: "postal_code");

            migrationBuilder.CreateIndex(
                name: "IX_repair_request_images_repair_request_id_display_order",
                table: "repair_request_images",
                columns: new[] { "repair_request_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_requests_assigned_technician_id_scheduled_start_at",
                table: "repair_requests",
                columns: new[] { "assigned_technician_id", "scheduled_start_at" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_requests_client_user_id_created_at",
                table: "repair_requests",
                columns: new[] { "client_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_requests_status_created_at",
                table: "repair_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spare_part_images_spare_part_id_display_order",
                table: "spare_part_images",
                columns: new[] { "spare_part_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_spare_parts_is_available_created_at",
                table: "spare_parts",
                columns: new[] { "is_available", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spare_parts_technician_user_id_created_at",
                table: "spare_parts",
                columns: new[] { "technician_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "postal_codes");

            migrationBuilder.DropTable(
                name: "repair_request_images");

            migrationBuilder.DropTable(
                name: "spare_part_images");

            migrationBuilder.DropTable(
                name: "chat_conversations");

            migrationBuilder.DropTable(
                name: "repair_requests");

            migrationBuilder.DropTable(
                name: "spare_parts");
        }
    }
}
