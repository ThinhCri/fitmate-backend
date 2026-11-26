using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayosOrderQrDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountName",
                table: "payos_orders",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "payos_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bin",
                table: "payos_orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "payos_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCode",
                table: "payos_orders",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountName",
                table: "payos_orders");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "payos_orders");

            migrationBuilder.DropColumn(
                name: "Bin",
                table: "payos_orders");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "payos_orders");

            migrationBuilder.DropColumn(
                name: "QrCode",
                table: "payos_orders");

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000001"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ thân người thành một đường thẳng.", "Hạ ngực có kiểm soát rồi đẩy người lên." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000002"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng chân rộng bằng vai, mũi chân hơi hướng ra ngoài.", "Đẩy hông ra sau, giữ lưng trung lập và đứng lên bằng cả bàn chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000003"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống khuỷu tay dưới vai và duỗi thẳng chân.", "Siết bụng, không võng lưng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000004"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000005"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bắt đầu ở tư thế hít đất cao.", "Luân phiên kéo gối về ngực, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string> { "quadriceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000006"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co một gối và duỗi chân còn lại.", "Siết mông nâng hông, giữ khung chậu cân bằng rồi hạ chậm." }, new List<string> { "glutes" }, new List<string> { "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000007"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co gối và giữ lưng dưới trên sàn.", "Dùng cơ bụng nâng vai, tránh kéo cổ." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000008"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng thẳng, giữ tay cầm dây hai bên hông.", "Xoay dây bằng cổ tay và bật nhảy nhẹ bằng mũi chân." }, new List<string> { "quadriceps" }, new List<string> { "calves", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000009"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, giữ tạ hai bên ngực.", "Đẩy tạ lên có kiểm soát và không khóa khuỷu tay quá mức." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000010"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Gập hông, giữ lưng trung lập và hai tay cầm tạ.", "Kéo khuỷu tay về sau, ép bả vai rồi hạ tạ chậm." }, new List<string> { "middle back" }, new List<string> { "biceps", "lats" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000011"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ tạ ngang vai, cổ tay thẳng.", "Đẩy tạ lên trên đầu và hạ về vị trí ban đầu có kiểm soát." }, new List<string> { "shoulders" }, new List<string> { "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000012"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ khuỷu tay sát thân người và cổ tay trung lập.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, new List<string> { "biceps" }, new List<string> { "forearms" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000013"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ khuỷu tay sát thân và thân người ổn định.", "Duỗi khuỷu tay kéo tay cầm xuống rồi trở lại chậm." }, new List<string> { "triceps" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000014"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, new List<string> { "lats" }, new List<string> { "biceps", "middle back", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000015"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng sát thanh đòn, gập hông và giữ cột sống trung lập.", "Đạp sàn để đứng lên, giữ thanh gần thân người." }, new List<string> { "hamstrings" }, new List<string> { "glutes", "lower back", "quadriceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000016"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đặt thanh chắc trên lưng trên và siết thân giữa.", "Hạ người có kiểm soát, đầu gối cùng hướng mũi chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000017"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Thực hiện một lần hít đất với thân người ổn định.", "Xoay người mở một tay lên trần, trở về giữa rồi đổi bên." }, new List<string> { "chest" }, new List<string> { "abdominals", "shoulders", "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000018"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000019"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống tay lên bề mặt chắc chắn cao hơn sàn.", "Giữ thân thẳng, hạ ngực rồi đẩy người lên có kiểm soát." }, new List<string> { "chest" }, new List<string> { "shoulders", "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000020"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nắm xà với lòng bàn tay hướng về người.", "Kéo ngực về phía xà, tránh đung đưa thân người." }, new List<string> { "lats" }, new List<string> { "biceps", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000021"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi hơi ngả thân, giữ cột sống trung lập.", "Xoay thân sang hai bên bằng cơ bụng, không giật vai." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000022"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, hai chân duỗi thẳng.", "Nâng chân bằng cơ bụng rồi hạ chậm, không cong lưng dưới." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000023"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Tựa lưng trên vào ghế và đặt thanh đòn chắc trên hông.", "Đẩy hông lên bằng cơ mông rồi hạ xuống có kiểm soát." }, new List<string> { "glutes" }, new List<string> { "calves", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000024"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, đặt thanh đòn có đệm trên đùi.", "Nhón gót hết biên độ rồi hạ chậm." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000025"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng giữa dây, giữ tay cầm hai bên.", "Dang tay đến ngang vai, giữ khuỷu hơi cong rồi hạ chậm." }, new List<string> { "shoulders" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000026"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người trên xà với tay duỗi.", "Hạ và kéo bả vai để nâng thân nhẹ mà không gập khuỷu tay." }, new List<string> { "traps" }, new List<string> { "lats", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000027"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng ngang máy cáp và giữ tay cầm bằng hai tay.", "Xoay thân khỏi máy bằng cơ bụng, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000028"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Điều chỉnh đệm máy nằm chắc trên đùi.", "Nhón gót hết biên độ, dừng ngắn rồi hạ có kiểm soát." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000029"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Cố định dây hỗ trợ vào xà và đặt chân hoặc gối vào dây.", "Kéo ngực lên xà, giữ thân ổn định rồi hạ chậm." }, new List<string> { "lats" }, new List<string> { "abdominals", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000030"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người chắc trên xà, siết vai và thân giữa.", "Nâng chân bằng cơ bụng, tránh đung đưa và hạ chậm." }, new List<string> { "abdominals" }, new List<string>() });
        }
    }
}
