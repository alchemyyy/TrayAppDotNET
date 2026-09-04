from __future__ import annotations

import importlib.util
import json
import os
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from types import ModuleType, SimpleNamespace
from unittest import mock
from zipfile import ZipFile


def load_publish_module() -> ModuleType:
    script_path = Path(__file__).resolve().parents[1] / "publish.py"
    specification = importlib.util.spec_from_file_location("trayapp_publish", script_path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"Could not load publish script: {script_path}")

    module = importlib.util.module_from_spec(specification)
    sys.modules[specification.name] = module
    specification.loader.exec_module(module)
    return module


PUBLISH = load_publish_module()


class PublishScriptTests(unittest.TestCase):
    def test_native_aot_apps_use_sdk_selected_ilcompiler(self) -> None:
        for app in PUBLISH.APPS:
            project_path: Path = PUBLISH.REPO_ROOT / app.project
            project_root: ET.Element = ET.parse(project_path).getroot()
            package_references: list[str] = [
                package_reference.attrib.get("Include", "")
                for package_reference in project_root.findall(".//PackageReference")
            ]

            self.assertNotIn(
                "Microsoft.DotNet.ILCompiler",
                package_references,
                f"{app.name} must use the ILCompiler selected by the active .NET SDK.",
            )

    def test_native_aot_restore_matches_publish_properties(self) -> None:
        app = next(
            app for app in PUBLISH.APPS if app.name == "FanControlTrayAppDotNET"
        )

        command = PUBLISH.restore_command(app, PUBLISH.PROFILES["release"])

        self.assertIn("-p:Configuration=Release", command)
        self.assertIn("-p:PublishAot=true", command)

    def test_generator_restores_match_publish_configuration(self) -> None:
        commands = PUBLISH.generator_restore_commands()

        self.assertTrue(commands)
        for command in commands:
            self.assertIn("-p:Configuration=Release", command)

    def test_publish_command_embeds_ephemeral_version_and_commit_hash(self) -> None:
        app = PUBLISH.APPS[0]
        commit_hash = "a" * 40

        command = PUBLISH.publish_command(
            app,
            Path("publish"),
            PUBLISH.PROFILES["release"],
            321,
            commit_hash,
        )

        self.assertIn("-p:BuildNumber=321", command)
        self.assertIn(f"-p:TrayAppDotNETCommitHash={commit_hash}", command)

    def test_native_aot_publish_validation_requires_embedded_dlls(self) -> None:
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]
        with tempfile.TemporaryDirectory() as temporary_directory:
            publish_directory = Path(temporary_directory)
            executable_path = publish_directory / f"{app.name}.exe"
            executable_path.write_bytes(b"native executable")
            (publish_directory / "av_libglesv2.dll").write_bytes(b"ANGLE")

            with mock.patch.object(PUBLISH, "validate_legal_directory"):
                PUBLISH.validate_publish_dir(app, publish_directory, profile)

            (publish_directory / "libSkiaSharp.dll").write_bytes(b"legacy")
            with (
                mock.patch.object(PUBLISH, "validate_legal_directory"),
                self.assertRaisesRegex(SystemExit, "must be embedded"),
            ):
                PUBLISH.validate_publish_dir(app, publish_directory, profile)

    def test_versions_manifest_supplies_reused_app_commit_hash(self) -> None:
        app = PUBLISH.APPS[0]
        commit_hash = "b" * 40
        with tempfile.TemporaryDirectory() as temporary_directory:
            manifest_path = Path(temporary_directory) / "versions.xml"
            manifest_path.write_text(
                """<?xml version="1.0" encoding="utf-8"?>
<versions>
  <artifacts>
    <artifact profile="release" kind="app" appId="BatteryTrayAppDotNET"
              version="7" commitHash="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" />
  </artifacts>
</versions>
""",
                encoding="utf-8",
            )

            actual_commit_hash = PUBLISH.app_commit_hash_from_versions_manifest(
                manifest_path,
                app,
                PUBLISH.PROFILES["release"],
                7,
            )

        self.assertEqual(commit_hash, actual_commit_hash)

    def test_latest_app_asset_can_come_from_older_release(self) -> None:
        app = PUBLISH.APPS[0]
        releases = [
            {
                "tag_name": "TrayAppDotNET_101",
                "assets": [{"name": "BrightnessTrayAppDotNET_8.zip"}],
            },
            {
                "tag_name": "TrayAppDotNET_100",
                "assets": [{"name": f"{app.name}_7.zip", "digest": "sha256:abc"}],
            },
        ]

        asset = PUBLISH.latest_published_app_asset(
            releases,
            app,
            PUBLISH.PROFILES["release"],
        )

        self.assertIsNotNone(asset)
        self.assertEqual("TrayAppDotNET_100", asset.release_tag)
        self.assertEqual(7, asset.version)
        self.assertEqual("sha256:abc", asset.digest)

    def test_latest_tray_release_requires_an_aggregate_asset(self) -> None:
        releases = [
            {
                "tag_name": "TrayAppDotNET_101",
                "assets": [{"name": "BatteryTrayAppDotNET_8.zip"}],
            },
            {
                "tag_name": "TrayAppDotNET_100",
                "assets": [{"name": "TrayAppDotNET_100.zip"}],
            },
        ]

        release = PUBLISH.latest_published_tray_release(
            releases,
            PUBLISH.PROFILES["release"],
        )

        self.assertIsNotNone(release)
        self.assertEqual("TrayAppDotNET_100", release["tag_name"])

    def test_default_tray_version_has_200_floor(self) -> None:
        releases = (
            None,
            {"tag_name": "TrayAppDotNET_127"},
            {"tag_name": "TrayAppDotNET_200"},
        )
        expected_versions = (200, 200, 201)

        for release, expected_version in zip(releases, expected_versions, strict=True):
            with (
                self.subTest(release=release),
                mock.patch.object(PUBLISH, "latest_release", return_value=release),
            ):
                self.assertEqual(expected_version, PUBLISH.default_tray_version("owner/repository"))

    def test_latest_reachable_release_tag_uses_highest_numeric_version(self) -> None:
        result = SimpleNamespace(
            returncode=0,
            stdout=(
                "TrayAppDotNET_9\n"
                "TrayAppDotNET_127\n"
                "TrayAppDotNET_invalid\n"
                "TrayAppDotNET_126\n"
            ),
        )

        with mock.patch.object(PUBLISH, "run", return_value=result) as run:
            tag = PUBLISH.latest_reachable_release_tag("target")

        self.assertEqual("TrayAppDotNET_127", tag)
        run.assert_called_once_with(
            [
                "git",
                "tag",
                "--merged",
                "target",
                "--list",
                "TrayAppDotNET_*",
            ],
            capture=True,
            check=False,
        )

    def test_change_detection_stops_after_one_commit_and_excludes_paths(self) -> None:
        result = SimpleNamespace(stdout="changed-commit\n")
        with mock.patch.object(PUBLISH, "run", return_value=result) as run:
            changed = PUBLISH.has_changes_since(
                "base",
                "target",
                ["FirstTrayAppDotNET"],
                ["FirstTrayAppDotNET/buildnumber.txt"],
            )

        self.assertTrue(changed)
        run.assert_called_once_with(
            [
                "git",
                "rev-list",
                "--max-count=1",
                "base..target",
                "--",
                "FirstTrayAppDotNET",
                ":(exclude)FirstTrayAppDotNET/buildnumber.txt",
            ],
            capture=True,
        )

    def test_plan_starts_unpublished_apps_at_version_200(self) -> None:
        app = PUBLISH.App(
            "FirstTrayAppDotNET",
            "first",
            "First",
            "FirstTrayAppDotNET/src/FirstTrayAppDotNET.csproj",
            "FirstTrayAppDotNET/buildnumber.txt",
        )
        arguments = SimpleNamespace(
            force_apps="",
            skip_apps="",
            target="HEAD",
            repo="owner/repository",
            force_rebuild=False,
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            original_directory = Path.cwd()
            os.chdir(temporary_directory)
            try:
                buildnumber_path = Path(app.buildnumber)
                buildnumber_path.parent.mkdir(parents=True, exist_ok=True)
                buildnumber_path.write_text("0", encoding="utf-8")
                outputs: dict[str, str] = {}

                with (
                    mock.patch.object(PUBLISH, "APPS", [app]),
                    mock.patch.object(PUBLISH, "published_releases", return_value=[]),
                    mock.patch.object(PUBLISH, "resolve_git_commit", return_value="target"),
                    mock.patch.object(
                        PUBLISH,
                        "set_github_output",
                        side_effect=lambda name, value: outputs.__setitem__(name, value),
                    ),
                ):
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))

                self.assertEqual("0", buildnumber_path.read_text(encoding="utf-8"))
                self.assertEqual("build", outputs["first_action"])
                self.assertEqual("200", outputs["first_version"])
            finally:
                os.chdir(original_directory)

    def test_plan_is_idempotent_and_supports_force_reuse_and_skip(self) -> None:
        first_app = PUBLISH.App(
            "FirstTrayAppDotNET",
            "first",
            "First",
            "FirstTrayAppDotNET/src/FirstTrayAppDotNET.csproj",
            "FirstTrayAppDotNET/buildnumber.txt",
        )
        second_app = PUBLISH.App(
            "SecondTrayAppDotNET",
            "second",
            "Second",
            "SecondTrayAppDotNET/src/SecondTrayAppDotNET.csproj",
            "SecondTrayAppDotNET/buildnumber.txt",
        )
        releases = [
            {
                "tag_name": "TrayAppDotNET_100",
                "assets": [
                    {"name": "TrayAppDotNET_100.zip"},
                    {"name": "FirstTrayAppDotNET_10.zip"},
                    {"name": "SecondTrayAppDotNET_7.zip"},
                ],
            }
        ]
        arguments = SimpleNamespace(
            force_apps="FirstTrayAppDotNET,SecondTrayAppDotNET",
            skip_apps="SecondTrayAppDotNET",
            target="HEAD",
            repo="owner/repository",
            force_rebuild=False,
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            original_directory = Path.cwd()
            os.chdir(temporary_directory)
            try:
                for app, version in ((first_app, 10), (second_app, 7)):
                    buildnumber_path = Path(app.buildnumber)
                    buildnumber_path.parent.mkdir(parents=True, exist_ok=True)
                    buildnumber_path.write_text(str(version), encoding="utf-8")

                changed_paths = {
                    first_app.name: True,
                }
                outputs: dict[str, str] = {}
                with (
                    mock.patch.object(PUBLISH, "APPS", [first_app, second_app]),
                    mock.patch.object(PUBLISH, "published_releases", return_value=releases),
                    mock.patch.object(
                        PUBLISH,
                        "resolve_git_commit",
                        side_effect=lambda reference: "target" if reference == "HEAD" else "base",
                    ),
                    mock.patch.object(PUBLISH, "require_ancestor"),
                    mock.patch.object(
                        PUBLISH,
                        "has_changes_since",
                        side_effect=lambda base, target, included, excluded: any(
                            changed_paths.get(path, False) for path in included
                        ),
                    ),
                    mock.patch.object(
                        PUBLISH,
                        "set_github_output",
                        side_effect=lambda name, value: outputs.__setitem__(name, value),
                    ),
                ):
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))
                    self.assertEqual("10", Path(first_app.buildnumber).read_text(encoding="utf-8"))
                    self.assertEqual("build", outputs["first_action"])
                    self.assertEqual("11", outputs["first_version"])
                    self.assertEqual("skip", outputs["second_action"])
                    self.assertEqual("", outputs["second_version"])
                    self.assertEqual("target", outputs["source_sha"])

                    outputs.clear()
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))
                    self.assertEqual("10", Path(first_app.buildnumber).read_text(encoding="utf-8"))
                    self.assertEqual("11", outputs["first_version"])

                    changed_paths.clear()
                    outputs.clear()
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))
                    self.assertEqual("build", outputs["first_action"])
                    self.assertEqual("10", outputs["first_version"])
                    self.assertEqual("skip", outputs["second_action"])

                    arguments.force_apps = ""
                    outputs.clear()
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))
                    self.assertEqual("reuse", outputs["first_action"])
                    self.assertEqual("10", outputs["first_version"])
            finally:
                os.chdir(original_directory)

    def test_common_change_builds_every_app_without_app_history_queries(self) -> None:
        first_app = PUBLISH.App(
            "FirstTrayAppDotNET",
            "first",
            "First",
            "FirstTrayAppDotNET/src/FirstTrayAppDotNET.csproj",
            "FirstTrayAppDotNET/buildnumber.txt",
        )
        second_app = PUBLISH.App(
            "SecondTrayAppDotNET",
            "second",
            "Second",
            "SecondTrayAppDotNET/src/SecondTrayAppDotNET.csproj",
            "SecondTrayAppDotNET/buildnumber.txt",
        )
        releases = [
            {
                "tag_name": "TrayAppDotNET_100",
                "assets": [
                    {"name": "TrayAppDotNET_100.zip"},
                    {"name": "FirstTrayAppDotNET_10.zip"},
                    {"name": "SecondTrayAppDotNET_7.zip"},
                ],
            }
        ]
        arguments = SimpleNamespace(
            force_apps="",
            skip_apps="",
            target="HEAD",
            repo="owner/repository",
            force_rebuild=False,
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            original_directory = Path.cwd()
            os.chdir(temporary_directory)
            try:
                for app, version in ((first_app, 0), (second_app, 0)):
                    buildnumber_path = Path(app.buildnumber)
                    buildnumber_path.parent.mkdir(parents=True, exist_ok=True)
                    buildnumber_path.write_text(str(version), encoding="utf-8")

                outputs: dict[str, str] = {}
                with (
                    mock.patch.object(PUBLISH, "APPS", [first_app, second_app]),
                    mock.patch.object(PUBLISH, "published_releases", return_value=releases),
                    mock.patch.object(
                        PUBLISH,
                        "resolve_git_commit",
                        side_effect=lambda reference: "target" if reference == "HEAD" else "tray-base",
                    ) as resolve_git_commit,
                    mock.patch.object(PUBLISH, "require_ancestor"),
                    mock.patch.object(PUBLISH, "has_changes_since", return_value=True) as has_changes,
                    mock.patch.object(
                        PUBLISH,
                        "set_github_output",
                        side_effect=lambda name, value: outputs.__setitem__(name, value),
                    ),
                ):
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))

                self.assertEqual("0", Path(first_app.buildnumber).read_text(encoding="utf-8"))
                self.assertEqual("0", Path(second_app.buildnumber).read_text(encoding="utf-8"))
                self.assertEqual("build", outputs["first_action"])
                self.assertEqual("build", outputs["second_action"])
                self.assertEqual("11", outputs["first_version"])
                self.assertEqual("8", outputs["second_version"])
                has_changes.assert_called_once_with(
                    "tray-base",
                    "target",
                    PUBLISH.SHARED_RELEASE_INPUT_PATHS,
                    [],
                )
                self.assertEqual(
                    [mock.call("HEAD"), mock.call("TrayAppDotNET_100")],
                    resolve_git_commit.call_args_list,
                )
            finally:
                os.chdir(original_directory)

    def test_unchanged_common_checks_each_app_from_its_own_release(self) -> None:
        first_app = PUBLISH.App(
            "FirstTrayAppDotNET",
            "first",
            "First",
            "FirstTrayAppDotNET/src/FirstTrayAppDotNET.csproj",
            "FirstTrayAppDotNET/buildnumber.txt",
        )
        second_app = PUBLISH.App(
            "SecondTrayAppDotNET",
            "second",
            "Second",
            "SecondTrayAppDotNET/src/SecondTrayAppDotNET.csproj",
            "SecondTrayAppDotNET/buildnumber.txt",
        )
        releases = [
            {
                "tag_name": "TrayAppDotNET_100",
                "assets": [
                    {"name": "TrayAppDotNET_100.zip"},
                    {"name": "FirstTrayAppDotNET_10.zip"},
                ],
            },
            {
                "tag_name": "TrayAppDotNET_99",
                "assets": [
                    {"name": "TrayAppDotNET_99.zip"},
                    {"name": "SecondTrayAppDotNET_7.zip"},
                ],
            },
        ]
        arguments = SimpleNamespace(
            force_apps="",
            skip_apps="",
            target="HEAD",
            repo="owner/repository",
            force_rebuild=False,
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            original_directory = Path.cwd()
            os.chdir(temporary_directory)
            try:
                for app, version in ((first_app, 10), (second_app, 7)):
                    buildnumber_path = Path(app.buildnumber)
                    buildnumber_path.parent.mkdir(parents=True, exist_ok=True)
                    buildnumber_path.write_text(str(version), encoding="utf-8")

                outputs: dict[str, str] = {}
                change_results = iter([False, True, False])
                with (
                    mock.patch.object(PUBLISH, "APPS", [first_app, second_app]),
                    mock.patch.object(PUBLISH, "published_releases", return_value=releases),
                    mock.patch.object(
                        PUBLISH,
                        "resolve_git_commit",
                        side_effect=lambda reference: "target" if reference == "HEAD" else "base",
                    ),
                    mock.patch.object(PUBLISH, "require_ancestor"),
                    mock.patch.object(
                        PUBLISH,
                        "has_changes_since",
                        side_effect=lambda *arguments: next(change_results),
                    ) as has_changes,
                    mock.patch.object(
                        PUBLISH,
                        "set_github_output",
                        side_effect=lambda name, value: outputs.__setitem__(name, value),
                    ),
                ):
                    self.assertEqual(0, PUBLISH.plan_publish(arguments))

                self.assertEqual("10", Path(first_app.buildnumber).read_text(encoding="utf-8"))
                self.assertEqual("7", Path(second_app.buildnumber).read_text(encoding="utf-8"))
                self.assertEqual("build", outputs["first_action"])
                self.assertEqual("11", outputs["first_version"])
                self.assertEqual("reuse", outputs["second_action"])
                self.assertEqual("7", outputs["second_version"])
                self.assertEqual(
                    [
                        mock.call(
                            "base",
                            "target",
                            PUBLISH.SHARED_RELEASE_INPUT_PATHS,
                            [],
                        ),
                        mock.call(
                            "base",
                            "target",
                            [first_app.name],
                            [first_app.buildnumber],
                        ),
                        mock.call(
                            "base",
                            "target",
                            [second_app.name, *PUBLISH.SHARED_RELEASE_INPUT_PATHS],
                            [second_app.buildnumber],
                        ),
                    ],
                    has_changes.call_args_list,
                )
            finally:
                os.chdir(original_directory)

    def test_reuse_takes_precedence_over_force_rebuild(self) -> None:
        expected_package = mock.sentinel.package
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]

        with mock.patch.object(
            PUBLISH,
            "download_published_app",
            return_value=expected_package,
        ) as download:
            package = PUBLISH.selected_app_package(
                "owner/repository",
                Path("output"),
                True,
                True,
                profile,
                app,
                None,
                "a" * 40,
            )

        self.assertIs(expected_package, package)
        download.assert_called_once_with(
            "owner/repository",
            Path("output"),
            profile,
            app,
        )

    def test_selected_app_collection_ignores_unselected_manifests(self) -> None:
        selected_app = PUBLISH.APPS[0]
        unselected_app = PUBLISH.APPS[1]

        with tempfile.TemporaryDirectory() as temporary_directory:
            input_root = Path(temporary_directory)
            for app, version in ((selected_app, 10), (unselected_app, 20)):
                app_root = input_root / app.name
                app_root.mkdir(parents=True, exist_ok=True)
                zip_name = f"{app.name}_{version}.zip"
                with ZipFile(app_root / zip_name, "w"):
                    pass
                manifest = {
                    "profile": "release",
                    "displayName": "Release",
                    "app": {
                        "appId": app.name,
                        "version": version,
                        "fileName": zip_name,
                        "sha256": "test",
                        "size": 0,
                        "source": "test",
                    },
                }
                (app_root / f"app-release-{app.name}.json").write_text(
                    json.dumps(manifest),
                    encoding="utf-8",
                )

            groups = PUBLISH.load_collected_profiles(
                input_root,
                ["release"],
                [selected_app],
            )

        self.assertEqual(
            [selected_app.name],
            [app["appId"] for app in groups["release"]["apps"]],
        )

    def test_legal_archive_validation_requires_license_notices_and_license_texts(self) -> None:
        valid_entries = [
            "LICENSE.txt",
            "NOTICE",
            *PUBLISH.REQUIRED_THIRD_PARTY_LICENSE_ARCHIVE_FILES,
        ]
        PUBLISH.validate_legal_archive_entries(
            valid_entries,
            "test archive",
        )

        with self.assertRaisesRegex(SystemExit, r"\.notices/"):
            PUBLISH.validate_legal_archive_entries(
                [
                    "LICENSE.txt",
                    "NOTICE",
                ],
                "test archive",
            )

    def test_third_party_notice_references_every_shipped_license_file(self) -> None:
        notice_text = (PUBLISH.REPO_ROOT / "NOTICE").read_text(
            encoding="utf-8"
        )

        for archive_name in PUBLISH.REQUIRED_THIRD_PARTY_LICENSE_ARCHIVE_FILES:
            self.assertIn(archive_name, notice_text)

    def test_release_notes_include_checksums_and_pull_requests(self) -> None:
        rows = [
            {
                "profile": "Release",
                "kind": "app",
                "appId": "BatteryTrayAppDotNET",
                "version": 10,
                "fileName": "BatteryTrayAppDotNET_10.zip",
                "sha256": "abc123",
                "source": "built-windows-native-aot",
                "commitHash": "a" * 40,
            }
        ]
        pull_requests = [
            PUBLISH.PullRequestEntry(
                42,
                "Fix release logic",
                "author",
                "https://github.com/owner/repository/pull/42",
            )
        ]

        with tempfile.TemporaryDirectory() as temporary_directory:
            notes_path = Path(temporary_directory) / "release-notes.md"
            PUBLISH.write_notes(
                notes_path,
                rows,
                "owner/repository",
                {"tag_name": "TrayAppDotNET_100"},
                [],
                pull_requests,
            )
            notes = notes_path.read_text(encoding="utf-8")

        self.assertIn("## Version Info", notes)
        self.assertIn("| Asset | Commit Hash |", notes)
        self.assertNotIn("SHA-256", notes)
        self.assertIn(f"`{'a' * 40}`", notes)
        self.assertNotIn("`abc123`", notes)
        self.assertIn("## Pull Requests", notes)
        self.assertNotIn("## Source Code", notes)
        self.assertNotIn("TrayAppDotNET_Source_", notes)
        self.assertIn(
            "- Fix release logic by author in https://github.com/owner/repository/pull/42",
            notes,
        )

    def test_release_notes_are_truncated_below_github_limit(self) -> None:
        rows = [
            {
                "profile": "Release",
                "kind": "aggregate",
                "appId": "TrayAppDotNET",
                "version": 200,
                "fileName": "TrayAppDotNET_200.zip",
                "sha256": "abc123",
                "source": "built-windows-native-aot",
                "commitHash": "a" * 40,
            }
        ]
        global_paths = tuple(
            ["TrayAppDotNETCommon/src/Common.cs"]
            + [f"{app.name}/src/App.cs" for app in PUBLISH.APPS]
        )
        commits = [
            PUBLISH.CommitEntry(
                f"{index:040x}",
                f"{index:07x}",
                f"Large commit {index} " + "x" * 500,
                global_paths,
            )
            for index in range(500)
        ]

        with tempfile.TemporaryDirectory() as temporary_directory:
            notes_path = Path(temporary_directory) / "release-notes.md"
            PUBLISH.write_notes(
                notes_path,
                rows,
                "owner/repository",
                None,
                commits,
                [],
            )
            notes = notes_path.read_text(encoding="utf-8")

        self.assertLessEqual(len(notes), PUBLISH.MAX_RELEASE_NOTES_CHARACTERS)
        self.assertIn(PUBLISH.RELEASE_NOTES_TRUNCATION_NOTICE, notes)
        self.assertIn("## Version Info", notes)

    def test_release_notes_group_commits_by_common_and_app(self) -> None:
        common_commit = PUBLISH.CommitEntry(
            "a" * 40,
            "a" * 7,
            "Change shared behavior",
            ("TrayAppDotNETCommon/src/Common.cs",),
        )
        multi_app_commit = PUBLISH.CommitEntry(
            "b" * 40,
            "b" * 7,
            "Change battery and volume",
            (
                "BatteryTrayAppDotNET/src/Battery.cs",
                "VolumeTrayAppDotNET/src/Volume.cs",
            ),
        )
        global_commit = PUBLISH.CommitEntry(
            "c" * 40,
            "c" * 7,
            "Change every app and common",
            tuple(
                ["TrayAppDotNETCommon/src/Common.cs"]
                + [f"{app.name}/src/App.cs" for app in PUBLISH.APPS]
            ),
        )

        sections = "\n".join(
            PUBLISH.commit_sections(
                "owner/repository",
                [common_commit, multi_app_commit, global_commit],
            )
        )

        headings = [
            line for line in sections.splitlines() if line.startswith("<div><b>")
        ]
        self.assertEqual(
            [
                "<div><b>Global</b></div>",
                "<div><b>Common</b></div>",
                "<div><b>BatteryTrayAppDotNET</b></div>",
                "<div><b>BrightnessTrayAppDotNET</b></div>",
                "<div><b>FanControlTrayAppDotNET</b></div>",
                "<div><b>NetworkTrayAppDotNET</b></div>",
                "<div><b>TaskManagerTrayAppDotNET</b></div>",
                "<div><b>VolumeTrayAppDotNET</b></div>",
            ],
            headings,
        )
        self.assertIn("<ul>", sections)
        self.assertIn("  <li>No commits.</li>", sections)
        self.assertIn(
            '<li><a href="https://github.com/owner/repository/commit/',
            sections,
        )
        self.assertIn("<code>aaaaaaa</code></a> Change shared behavior</li>", sections)
        self.assertEqual(1, sections.count("Change shared behavior"))
        self.assertEqual(2, sections.count("Change battery and volume"))
        self.assertEqual(1, sections.count("Change every app and common"))
        global_section, common_section = sections.split(
            "<div><b>Common</b></div>", 1
        )
        self.assertIn("Change every app and common", global_section)
        self.assertNotIn("Change every app and common", common_section)

    def test_commits_since_release_collects_changed_paths(self) -> None:
        result = SimpleNamespace(
            returncode=0,
            stdout=(
                "\x1e" + "a" * 40 + "\taaaaaaa\tFirst commit\n\n"
                "BatteryTrayAppDotNET/src/First.cs\n"
                "VolumeTrayAppDotNET/src/Second.cs\n"
                "\x1e" + "b" * 40 + "\tbbbbbbb\tSecond commit\n\n"
                "TrayAppDotNETCommon/src/Common.cs\n"
            ),
        )
        with mock.patch.object(PUBLISH, "run", return_value=result):
            commits = PUBLISH.commits_since_release(
                {"tag_name": "TrayAppDotNET_100"},
                "target",
            )

        self.assertEqual(
            (
                "BatteryTrayAppDotNET/src/First.cs",
                "VolumeTrayAppDotNET/src/Second.cs",
            ),
            commits[0].paths,
        )
        self.assertEqual(("TrayAppDotNETCommon/src/Common.cs",), commits[1].paths)

if __name__ == "__main__":
    unittest.main()
