using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medical.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictDoctorShiftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Doctors_DoctorId",
                table: "Shifts");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Doctors_DoctorId",
                table: "Shifts",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Doctors_DoctorId",
                table: "Shifts");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Doctors_DoctorId",
                table: "Shifts",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
