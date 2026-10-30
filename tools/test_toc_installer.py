"""Guard against shipping a ToC installer without the offline SQLite catalog."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class TocInstallerPackagingTests(unittest.TestCase):
    def test_inno_setup_installs_sqlite_database(self):
        script = (ROOT / "installer.iss").read_text(encoding="utf-8-sig")
        self.assertIn("[Files]", script)
        files_section = script.split("[Files]", 1)[1].split("[Icons]", 1)[0]
        entries = [
            line.strip()
            for line in files_section.splitlines()
            if line.strip() and not line.lstrip().startswith(";")
        ]
        self.assertIn(
            r'Source: "artifacts\publish\Data\*.sqlite"; DestDir: "{app}\Data"; Flags: ignoreversion',
            entries,
            "The published SQLite catalog must be copied into the installed Data directory.",
        )

    def test_project_publishes_sqlite_database(self):
        project = (ROOT / "RoAura.csproj").read_text(encoding="utf-8-sig")
        self.assertIn(r'<Content Include="Data\*.sqlite">', project)
        self.assertIn("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>", project)


if __name__ == "__main__":
    unittest.main()
