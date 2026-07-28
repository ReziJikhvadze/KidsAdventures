import xlsx from "xlsx";

const file = "C:/Users/Relloran/Downloads/Future_Navigator_Automated_Career_System_V2_with_Examples_FIXED.xlsx";
const wb = xlsx.readFile(file);

console.log("SHEETS:", wb.SheetNames);
for (const name of wb.SheetNames) {
  const ws = wb.Sheets[name];
  const rows = xlsx.utils.sheet_to_json(ws, { header: 1, defval: "" });
  console.log("\n\n===== SHEET:", name, "=====  rows:", rows.length);
  const max = Math.min(rows.length, 60);
  for (let i = 0; i < max; i++) {
    console.log(i, JSON.stringify(rows[i]));
  }
}
