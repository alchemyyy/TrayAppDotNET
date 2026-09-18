from __future__ import annotations

import hashlib
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


def command_output_path(command: list[str]) -> Path:
    return Path(command[command.index("--output") + 1])


FACTORY_BYTES = b"factory"
# A stamped installer has to clear PUBLISH.INSTALLER_MINIMUM_SIZE_BYTES, so the fake image is padded
# past that floor.
STAMPED_INSTALLER_BYTES = b"installer-with-payload".ljust(
    PUBLISH.INSTALLER_MINIMUM_SIZE_BYTES + 1, b"\0"
)
VERIFY_INSTALLER_STDOUT = "verified 1 payload(s)\n"
VERIFY_INSTALLER_STDERR = "payload directory hash mismatch\n"
# The installer factory is a .NET Framework 4.8 build, so none of these .NET publishing
# properties may appear on its restore or publish command line.
INSTALLER_REMOVED_PROPERTY_PREFIXES = (
    "-p:PublishAot=",
    "-p:SelfContained=",
    "-p:UseAppHost=",
    "-p:EnableWindowsTargeting=",
    "-p:PublishSingleFile=",
    "-p:RuntimeIdentifier=",
)


def command_payload_paths(command: list[str]) -> list[str]:
    return [
        command[index + 1]
        for index, argument in enumerate(command)
        if argument == "--payload"
    ]


def installer_verify_command(factory_path: Path, output_path: Path) -> list[str]:
    return [
        str(factory_path.resolve()),
        "--verify-installer",
        "--image",
        str(output_path.resolve()),
    ]


def write_fake_installer_factory_publish(
    command: list[str], extra_file_names: tuple[str, ...]
) -> None:
    publish_dir = command_output_path(command)
    publish_dir.mkdir(parents=True, exist_ok=True)
    (publish_dir / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME).write_bytes(FACTORY_BYTES)
    for extra_file_name in extra_file_names:
        extra_path = publish_dir / extra_file_name
        extra_path.parent.mkdir(parents=True, exist_ok=True)
        extra_path.write_bytes(b"extra")


def write_fake_stamped_installer(command: list[str], installer_bytes: bytes) -> None:
    output_path = command_output_path(command)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(installer_bytes)


def fake_installer_run(
    extra_file_names: tuple[str, ...] = (),
    installer_bytes: bytes = STAMPED_INSTALLER_BYTES,
    verify_returncode: int = 0,
) -> tuple[list[list[str]], object]:
    """Fakes the factory publish, every factory stamp and every factory verification."""
    commands: list[list[str]] = []

    def run(cmd: list[str], **_keyword_arguments) -> SimpleNamespace:
        commands.append(cmd)
        if cmd[0] == "dotnet" and cmd[1] == "publish":
            write_fake_installer_factory_publish(cmd, extra_file_names)
        elif "--make-installer" in cmd:
            write_fake_stamped_installer(cmd, installer_bytes)
        elif "--verify-installer" in cmd and verify_returncode != 0:
            return SimpleNamespace(
                returncode=verify_returncode,
                stdout="",
                stderr=VERIFY_INSTALLER_STDERR,
            )
        elif "--verify-installer" in cmd:
            return SimpleNamespace(
                returncode=0, stdout=VERIFY_INSTALLER_STDOUT, stderr=""
            )
        return SimpleNamespace(returncode=0, stdout="", stderr="")

    return commands, run


def write_fake_native_aot_publish(publish_dir: Path, app: PUBLISH.App) -> None:
    legal_names = [
        *PUBLISH.REQUIRED_LEGAL_ARCHIVE_FILES,
        *PUBLISH.REQUIRED_THIRD_PARTY_LICENSE_ARCHIVE_FILES,
    ]
    for legal_name in legal_names:
        legal_path = publish_dir / legal_name
        legal_path.parent.mkdir(parents=True, exist_ok=True)
        legal_path.write_bytes(b"legal")
    (publish_dir / f"{app.name}.exe").write_bytes(b"exe")
    for dll_name in PUBLISH.REQUIRED_LOOSE_NATIVE_AOT_DLL_NAMES:
        (publish_dir / dll_name).write_bytes(b"dll")


def write_collected_app_manifest(
    app_root: Path,
    app: PUBLISH.App,
    version: int,
    *,
    write_installer: bool = True,
    include_installer_fields: bool = True,
) -> Path:
    app_root.mkdir(parents=True, exist_ok=True)
    zip_name = f"{app.name}_{version}.zip"
    with ZipFile(app_root / zip_name, "w"):
        pass
    installer_name = PUBLISH.installer_asset_name(app.name)
    if write_installer:
        (app_root / installer_name).write_bytes(b"installer")

    app_data = {
        "appId": app.name,
        "version": version,
        "fileName": zip_name,
        "sha256": "test",
        "size": 0,
        "source": "test",
    }
    if include_installer_fields:
        app_data["installerFileName"] = installer_name
        app_data["installerSha256"] = "installer-test"
        app_data["installerSize"] = len(b"installer")

    manifest = {"profile": "release", "displayName": "Release", "app": app_data}
    manifest_path = app_root / f"app-release-{app.name}.json"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    return manifest_path


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
                write_collected_app_manifest(input_root / app.name, app, version)

            groups = PUBLISH.load_collected_profiles(
                input_root,
                ["release"],
                [selected_app],
            )

        self.assertEqual(
            [selected_app.name],
            [app["appId"] for app in groups["release"]["apps"]],
        )
        self.assertEqual(
            input_root
            / selected_app.name
            / PUBLISH.installer_asset_name(selected_app.name),
            groups["release"]["apps"][0]["installerPath"],
        )

    def test_selected_app_collection_requires_installer_fields_and_exe(self) -> None:
        app = PUBLISH.APPS[0]

        with tempfile.TemporaryDirectory() as temporary_directory:
            input_root = Path(temporary_directory)
            write_collected_app_manifest(
                input_root / app.name, app, 10, write_installer=False
            )
            with self.assertRaisesRegex(SystemExit, r"Missing built installer"):
                PUBLISH.load_collected_profiles(input_root, ["release"], [app])

            write_collected_app_manifest(
                input_root / app.name, app, 10, include_installer_fields=False
            )
            with self.assertRaisesRegex(
                SystemExit, r"installerFileName, installerSha256, installerSize"
            ):
                PUBLISH.load_collected_profiles(input_root, ["release"], [app])

    def test_staged_app_manifest_requires_installer_exe(self) -> None:
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]

        with tempfile.TemporaryDirectory() as temporary_directory:
            package_dir = Path(temporary_directory)
            manifest_path = write_collected_app_manifest(
                package_dir, app, 10, write_installer=False
            )
            with self.assertRaisesRegex(SystemExit, r"Missing staged app installer"):
                PUBLISH.app_manifest_files(package_dir, profile, app)

            installer_path = package_dir / PUBLISH.installer_asset_name(app.name)
            installer_path.write_bytes(b"installer")
            _manifest_path, _manifest, files = PUBLISH.app_manifest_files(
                package_dir, profile, app
            )

        self.assertEqual(
            [manifest_path, package_dir / f"{app.name}_10.zip", installer_path],
            files,
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

    def test_every_app_has_an_installer_icon(self) -> None:
        # The factory picks the icon itself, but it can only do so while every app ships one.
        for app in PUBLISH.APPS:
            icon_path = PUBLISH.REPO_ROOT / app.name / "app.ico"
            self.assertTrue(
                icon_path.is_file(),
                f"{app.name} is missing app.ico for its installer.",
            )

    def test_installer_asset_names_use_installer_prefix(self) -> None:
        self.assertEqual(
            "Installer_BatteryTrayAppDotNET.exe",
            PUBLISH.installer_asset_name("BatteryTrayAppDotNET"),
        )
        self.assertEqual(
            "Installer_TrayAppDotNET.exe",
            PUBLISH.installer_asset_name(PUBLISH.BUNDLE_INSTALLER_NAME),
        )

    def test_installer_restore_command_matches_publish_properties(self) -> None:
        command = PUBLISH.installer_restore_command()

        self.assertEqual(["dotnet", "restore", PUBLISH.INSTALLER_PROJECT], command[:3])
        self.assertIn("-p:Configuration=Release", command)
        # The installer targets .NET Framework 4.8, so the restore graph has no runtime identifier
        # and none of the .NET publishing properties the applications restore with.
        self.assertNotIn("--runtime", command)
        self.assertNotIn("win-x64", command)
        for removed_prefix in INSTALLER_REMOVED_PROPERTY_PREFIXES:
            self.assertFalse(
                any(argument.startswith(removed_prefix) for argument in command),
                f"The installer restore must not pass {removed_prefix}.",
            )

    def test_installer_project_references_no_generator(self) -> None:
        # The generator restores belong to the application path only while this holds.
        project_root: ET.Element = ET.parse(
            PUBLISH.REPO_ROOT / PUBLISH.INSTALLER_PROJECT
        ).getroot()
        referenced_projects: list[str] = [
            project_reference.attrib.get("Include", "").replace("\\", "/")
            for project_reference in project_root.findall(".//ProjectReference")
        ]

        for generator_project in PUBLISH.GENERATOR_PROJECTS:
            generator_file_name = Path(generator_project).name
            self.assertFalse(
                any(
                    referenced_project.endswith(generator_file_name)
                    for referenced_project in referenced_projects
                ),
                f"The installer must not reference {generator_file_name}.",
            )

    def test_installer_factory_publish_command_carries_no_payload_or_icon(self) -> None:
        publish_dir = Path("installer") / "factory" / "publish"
        commit_hash = "c" * 40

        command = PUBLISH.installer_factory_publish_command(publish_dir, commit_hash)

        self.assertEqual(
            ["dotnet", "publish", PUBLISH.INSTALLER_PROJECT, "--no-restore"],
            command[:4],
        )
        self.assertEqual(str(publish_dir), command[command.index("--output") + 1])
        self.assertIn("-p:DebugType=none", command)
        self.assertIn("-p:DebugSymbols=false", command)
        self.assertIn("-p:ContinuousIntegrationBuild=true", command)
        self.assertIn(f"-p:TrayAppDotNETCommitHash={commit_hash}", command)
        for removed_prefix in (
            "-p:AssemblyName=",
            "-p:TrayAppDotNETInstallerPayloads=",
            "-p:TrayAppDotNETInstallerIcon=",
        ):
            self.assertFalse(
                any(argument.startswith(removed_prefix) for argument in command),
                f"The installer factory publish must not pass {removed_prefix}.",
            )

    def test_installer_factory_publish_command_has_no_runtime_or_aot_properties(
        self,
    ) -> None:
        command = PUBLISH.installer_factory_publish_command(Path("publish"), "d" * 40)

        # A plain .NET Framework 4.8 publish: no runtime identifier, no self-contained runtime
        # and no ahead-of-time compilation.
        for removed_argument in ("--runtime", "--self-contained", "win-x64"):
            self.assertNotIn(removed_argument, command)
        for removed_prefix in INSTALLER_REMOVED_PROPERTY_PREFIXES:
            self.assertFalse(
                any(argument.startswith(removed_prefix) for argument in command),
                f"The installer factory publish must not pass {removed_prefix}.",
            )

    def test_installer_factory_publish_command_omits_unknown_commit_hash(self) -> None:
        command = PUBLISH.installer_factory_publish_command(Path("publish"), "")

        self.assertFalse(
            any(
                argument.startswith("-p:TrayAppDotNETCommitHash=")
                for argument in command
            )
        )

    def test_build_installer_factory_publishes_a_single_file_exe(self) -> None:
        profile = PUBLISH.PROFILES["release"]
        commit_hash = "d" * 40

        with tempfile.TemporaryDirectory() as temporary_directory:
            output_root = Path(temporary_directory) / "output"
            commands, run = fake_installer_run(("TrayAppDotNETInstaller.pdb",))

            with mock.patch.object(PUBLISH, "run", side_effect=run):
                factory_path = PUBLISH.build_installer_factory(
                    output_root, profile, commit_hash
                )

            expected_publish_dir = (
                output_root / "release" / "installer" / "factory" / "publish"
            )
            self.assertEqual(
                expected_publish_dir / "TrayAppDotNETInstaller.exe", factory_path
            )
            self.assertEqual(FACTORY_BYTES, factory_path.read_bytes())
            self.assertFalse(
                (expected_publish_dir / "TrayAppDotNETInstaller.pdb").exists()
            )

        # The installer references no generator, so its own restore is the only one that runs.
        self.assertEqual(
            [
                PUBLISH.installer_restore_command(),
                PUBLISH.installer_factory_publish_command(
                    expected_publish_dir, commit_hash
                ),
            ],
            commands,
        )
        for generator_restore_command in PUBLISH.generator_restore_commands():
            self.assertNotIn(generator_restore_command, commands)

    def test_build_installer_factory_rejects_publish_with_leftover_dll(self) -> None:
        profile = PUBLISH.PROFILES["release"]

        with tempfile.TemporaryDirectory() as temporary_directory:
            output_root = Path(temporary_directory) / "output"
            _commands, run = fake_installer_run(
                ("TrayAppDotNETInstaller.resources.dll",)
            )

            with (
                mock.patch.object(PUBLISH, "run", side_effect=run),
                self.assertRaisesRegex(
                    SystemExit, r"TrayAppDotNETInstaller\.resources\.dll"
                ),
            ):
                PUBLISH.build_installer_factory(output_root, profile, "")

    def test_stamp_installer_runs_the_factory_with_one_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_path = root / "BatteryTrayAppDotNET_10.zip"
            payload_path.write_bytes(b"zip")
            output_path = root / "packages" / "Installer_BatteryTrayAppDotNET.exe"
            commands, run = fake_installer_run()

            with mock.patch.object(PUBLISH, "run", side_effect=run):
                installer_path = PUBLISH.stamp_installer(
                    factory_path,
                    "BatteryTrayAppDotNET",
                    [payload_path],
                    output_path,
                )

            self.assertEqual(output_path, installer_path)
            self.assertEqual(STAMPED_INSTALLER_BYTES, installer_path.read_bytes())
            self.assertEqual(
                [
                    [
                        str(factory_path.resolve()),
                        "--make-installer",
                        "--output",
                        str(output_path.resolve()),
                        "--payload",
                        str(payload_path.resolve()),
                    ],
                    installer_verify_command(factory_path, output_path),
                ],
                commands,
            )

    def test_stamp_installer_passes_every_payload_in_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_paths = []
            for file_name in (
                "BatteryTrayAppDotNET_10.zip",
                "BrightnessTrayAppDotNET_7.zip",
                "VolumeTrayAppDotNET_3.zip",
            ):
                payload_path = root / file_name
                payload_path.write_bytes(b"zip")
                payload_paths.append(payload_path)
            output_path = root / "_release" / "Installer_TrayAppDotNET.exe"
            commands, run = fake_installer_run()

            with mock.patch.object(PUBLISH, "run", side_effect=run):
                PUBLISH.stamp_installer(
                    factory_path,
                    PUBLISH.BUNDLE_INSTALLER_NAME,
                    payload_paths,
                    output_path,
                )

            self.assertEqual(2, len(commands))
            self.assertEqual(
                [str(payload_path.resolve()) for payload_path in payload_paths],
                command_payload_paths(commands[0]),
            )
            self.assertEqual(
                installer_verify_command(factory_path, output_path), commands[1]
            )
            self.assertTrue(output_path.is_file())

    def test_stamp_installer_requires_the_factory_executable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            payload_path = root / "BatteryTrayAppDotNET_10.zip"
            payload_path.write_bytes(b"zip")

            with (
                mock.patch.object(PUBLISH, "run") as run,
                self.assertRaisesRegex(SystemExit, r"installer factory not found"),
            ):
                PUBLISH.stamp_installer(
                    root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME,
                    "BatteryTrayAppDotNET",
                    [payload_path],
                    root / "Installer_BatteryTrayAppDotNET.exe",
                )

        run.assert_not_called()

    def test_stamp_installer_requires_every_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            present_payload_path = root / "BatteryTrayAppDotNET_10.zip"
            present_payload_path.write_bytes(b"zip")
            missing_payload_path = root / "VolumeTrayAppDotNET_7.zip"
            output_path = root / "Installer_TrayAppDotNET.exe"

            with mock.patch.object(PUBLISH, "run") as run:
                with self.assertRaisesRegex(SystemExit, r"at least one payload zip"):
                    PUBLISH.stamp_installer(
                        factory_path,
                        PUBLISH.BUNDLE_INSTALLER_NAME,
                        [],
                        output_path,
                    )
                with self.assertRaisesRegex(SystemExit, r"VolumeTrayAppDotNET_7\.zip"):
                    PUBLISH.stamp_installer(
                        factory_path,
                        PUBLISH.BUNDLE_INSTALLER_NAME,
                        [present_payload_path, missing_payload_path],
                        output_path,
                    )

            self.assertFalse(output_path.exists())

        run.assert_not_called()

    def test_stamp_installer_rejects_a_failed_verification(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_path = root / "BatteryTrayAppDotNET_10.zip"
            payload_path.write_bytes(b"zip")
            output_path = root / "Installer_BatteryTrayAppDotNET.exe"
            commands, run = fake_installer_run(verify_returncode=3)

            with (
                mock.patch.object(PUBLISH, "run", side_effect=run),
                self.assertRaisesRegex(
                    SystemExit, r"failed payload verification \(exit code 3\)"
                ),
            ):
                PUBLISH.stamp_installer(
                    factory_path,
                    "BatteryTrayAppDotNET",
                    [payload_path],
                    output_path,
                )

            self.assertEqual(
                installer_verify_command(factory_path, output_path), commands[-1]
            )

    def test_stamp_installer_accepts_an_installer_smaller_than_its_payloads(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_path = root / "BatteryTrayAppDotNET_10.zip"
            # The payloads are LZMA-recompressed while stamping, so a correct installer is routinely
            # smaller than the zips it was built from.
            payload_path.write_bytes(b"z" * (len(STAMPED_INSTALLER_BYTES) * 4))
            output_path = root / "Installer_BatteryTrayAppDotNET.exe"
            commands, run = fake_installer_run()

            with mock.patch.object(PUBLISH, "run", side_effect=run):
                installer_path = PUBLISH.stamp_installer(
                    factory_path,
                    "BatteryTrayAppDotNET",
                    [payload_path],
                    output_path,
                )

            self.assertEqual(output_path, installer_path)
            self.assertLess(
                installer_path.stat().st_size, payload_path.stat().st_size
            )
            self.assertEqual(
                installer_verify_command(factory_path, output_path), commands[-1]
            )

    def test_stamp_installer_rejects_an_undersized_installer(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_path = root / "BatteryTrayAppDotNET_10.zip"
            payload_path.write_bytes(b"zip")
            commands, run = fake_installer_run(
                installer_bytes=b"t" * PUBLISH.INSTALLER_MINIMUM_SIZE_BYTES
            )

            with (
                mock.patch.object(PUBLISH, "run", side_effect=run),
                self.assertRaisesRegex(
                    SystemExit, r"Refusing to publish a truncated installer"
                ),
            ):
                PUBLISH.stamp_installer(
                    factory_path,
                    "BatteryTrayAppDotNET",
                    [payload_path],
                    root / "Installer_BatteryTrayAppDotNET.exe",
                )

            # The floor is cheap, so it runs before the factory is asked to verify anything.
            self.assertNotIn("--verify-installer", commands[-1])

    def test_build_bundle_installer_stamps_every_app_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            payload_paths = []
            for file_name in (
                "BatteryTrayAppDotNET_10.zip",
                "VolumeTrayAppDotNET_7.zip",
            ):
                payload_path = root / file_name
                payload_path.write_bytes(b"zip")
                payload_paths.append(payload_path)
            final_dir = root / "_release"
            commands, run = fake_installer_run()

            with mock.patch.object(PUBLISH, "run", side_effect=run):
                bundle_installer_path = PUBLISH.build_bundle_installer(
                    factory_path, payload_paths, final_dir
                )

            self.assertEqual(
                final_dir / "Installer_TrayAppDotNET.exe", bundle_installer_path
            )
            self.assertEqual(
                [str(payload_path.resolve()) for payload_path in payload_paths],
                command_payload_paths(commands[0]),
            )

    def test_resolve_installer_factory_prefers_a_supplied_factory(self) -> None:
        profile = PUBLISH.PROFILES["release"]

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            factory_path = root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            built_factory_path = root / "built" / "TrayAppDotNETInstaller.exe"

            with mock.patch.object(
                PUBLISH, "build_installer_factory", return_value=built_factory_path
            ) as build_installer_factory:
                self.assertEqual(
                    factory_path,
                    PUBLISH.resolve_installer_factory(
                        str(factory_path), root, profile, "a" * 40
                    ),
                )
                build_installer_factory.assert_not_called()

                for requested_factory in ("", str(root / "missing.exe")):
                    self.assertEqual(
                        built_factory_path,
                        PUBLISH.resolve_installer_factory(
                            requested_factory, root, profile, "a" * 40
                        ),
                    )

                self.assertEqual(
                    [mock.call(root, profile, "a" * 40)] * 2,
                    build_installer_factory.call_args_list,
                )

    def test_build_app_profile_stamps_installer_with_a_supplied_factory(self) -> None:
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]
        commit_hash = "e" * 40

        with tempfile.TemporaryDirectory() as temporary_directory:
            output_root = Path(temporary_directory)
            package_dir = output_root / "release" / "packages"
            package_dir.mkdir(parents=True)
            zip_path = package_dir / f"{app.name}_5.zip"
            zip_path.write_bytes(b"zip")
            package = PUBLISH.AppPackage(
                app, profile, 5, zip_path, profile.build_source, commit_hash
            )
            factory_path = output_root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            factory_path.write_bytes(FACTORY_BYTES)
            installer_path = package_dir / PUBLISH.installer_asset_name(app.name)
            arguments = SimpleNamespace(
                app_name=app.name,
                profile="release",
                output_root=str(output_root),
                installer_factory=str(factory_path),
                app_version="5",
                repo="owner/repository",
                force_rebuild=False,
                reuse_latest=False,
            )
            commands, run = fake_installer_run()

            with (
                mock.patch.object(
                    PUBLISH, "try_resolve_git_commit", return_value=commit_hash
                ),
                mock.patch.object(
                    PUBLISH, "build_app", return_value=package
                ) as build_app,
                mock.patch.object(
                    PUBLISH, "build_installer_factory"
                ) as build_installer_factory,
                mock.patch.object(PUBLISH, "run", side_effect=run),
            ):
                self.assertEqual(0, PUBLISH.build_app_profile(arguments))

            manifest = json.loads(
                (package_dir / f"app-release-{app.name}.json").read_text(
                    encoding="utf-8"
                )
            )

            build_app.assert_called_once_with(app, 5, output_root, profile, commit_hash)
            build_installer_factory.assert_not_called()
            self.assertEqual(
                [
                    [
                        str(factory_path.resolve()),
                        "--make-installer",
                        "--output",
                        str(installer_path.resolve()),
                        "--payload",
                        str(zip_path.resolve()),
                    ],
                    installer_verify_command(factory_path, installer_path),
                ],
                commands,
            )
            self.assertEqual(zip_path.name, manifest["app"]["fileName"])
            self.assertEqual(installer_path.name, manifest["app"]["installerFileName"])
            self.assertEqual(
                hashlib.sha256(STAMPED_INSTALLER_BYTES).hexdigest(),
                manifest["app"]["installerSha256"],
            )
            self.assertEqual(
                len(STAMPED_INSTALLER_BYTES), manifest["app"]["installerSize"]
            )

    def test_build_app_profile_builds_a_factory_for_a_reused_package(self) -> None:
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]
        head_commit_hash = "e" * 40
        reused_commit_hash = "f" * 40

        with tempfile.TemporaryDirectory() as temporary_directory:
            output_root = Path(temporary_directory)
            package_dir = output_root / "release" / "packages"
            package_dir.mkdir(parents=True)
            zip_path = package_dir / f"{app.name}_5.zip"
            zip_path.write_bytes(b"zip")
            package = PUBLISH.AppPackage(
                app,
                profile,
                5,
                zip_path,
                "reused-TrayAppDotNET_100",
                reused_commit_hash,
            )
            factory_path = output_root / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            installer_path = package_dir / PUBLISH.installer_asset_name(app.name)
            arguments = SimpleNamespace(
                app_name=app.name,
                profile="release",
                output_root=str(output_root),
                installer_factory="",
                app_version="",
                repo="owner/repository",
                force_rebuild=False,
                reuse_latest=True,
            )
            commands, run = fake_installer_run()

            def fake_build_installer_factory(*_positional_arguments) -> Path:
                factory_path.write_bytes(FACTORY_BYTES)
                return factory_path

            with (
                mock.patch.object(
                    PUBLISH, "try_resolve_git_commit", return_value=head_commit_hash
                ),
                mock.patch.object(
                    PUBLISH, "download_published_app", return_value=package
                ) as download,
                mock.patch.object(PUBLISH, "build_app") as build_app,
                mock.patch.object(
                    PUBLISH,
                    "build_installer_factory",
                    side_effect=fake_build_installer_factory,
                ) as build_installer_factory,
                mock.patch.object(PUBLISH, "run", side_effect=run),
            ):
                self.assertEqual(0, PUBLISH.build_app_profile(arguments))

            manifest = json.loads(
                (package_dir / f"app-release-{app.name}.json").read_text(
                    encoding="utf-8"
                )
            )

            download.assert_called_once_with(
                "owner/repository", output_root, profile, app
            )
            build_app.assert_not_called()
            build_installer_factory.assert_called_once_with(
                output_root, profile, head_commit_hash
            )
            self.assertEqual(
                [
                    [
                        str(factory_path.resolve()),
                        "--make-installer",
                        "--output",
                        str(installer_path.resolve()),
                        "--payload",
                        str(zip_path.resolve()),
                    ],
                    installer_verify_command(factory_path, installer_path),
                ],
                commands,
            )
            self.assertEqual("reused-TrayAppDotNET_100", manifest["app"]["source"])
            self.assertEqual(reused_commit_hash, manifest["app"]["commitHash"])
            self.assertEqual(installer_path.name, manifest["app"]["installerFileName"])

    def test_profile_manifest_from_group_carries_installer_fields(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            final_dir = Path(temporary_directory)
            aggregate_zip = final_dir / "TrayAppDotNET_200.zip"
            aggregate_zip.write_bytes(b"aggregate")
            bundle_installer_path = final_dir / "Installer_TrayAppDotNET.exe"
            bundle_installer_path.write_bytes(b"bundle")
            zip_path = final_dir / "BatteryTrayAppDotNET_10.zip"
            zip_path.write_bytes(b"zip")
            installer_path = final_dir / "Installer_BatteryTrayAppDotNET.exe"
            installer_path.write_bytes(b"installer")
            group = {
                "profile": "release",
                "displayName": "Release",
                "runtime": "win-x64",
                "apps": [
                    {
                        "appId": "BatteryTrayAppDotNET",
                        "version": 10,
                        "fileName": zip_path.name,
                        "sha256": "app-sha",
                        "size": 3,
                        "source": "built-windows-native-aot",
                        "commitHash": "b" * 40,
                        "installerFileName": installer_path.name,
                        "installerSha256": "installer-sha",
                        "installerSize": 9,
                        "zipPath": zip_path,
                        "installerPath": installer_path,
                    }
                ],
            }

            manifest = PUBLISH.profile_manifest_from_group(
                group,
                aggregate_zip,
                "aggregate-sha",
                200,
                "a" * 40,
                bundle_installer_path,
            )

        self.assertEqual(
            {
                "fileName": "Installer_TrayAppDotNET.exe",
                "sha256": hashlib.sha256(b"bundle").hexdigest(),
                "size": len(b"bundle"),
            },
            manifest["bundleInstaller"],
        )
        self.assertEqual(
            "Installer_BatteryTrayAppDotNET.exe",
            manifest["apps"][0]["installerFileName"],
        )
        self.assertEqual("installer-sha", manifest["apps"][0]["installerSha256"])
        self.assertEqual(9, manifest["apps"][0]["installerSize"])

    def test_artifact_rows_include_installer_rows_after_package_rows(self) -> None:
        manifest = {
            "profile": "release",
            "displayName": "Release",
            "version": 200,
            "aggregate": {
                "fileName": "TrayAppDotNET_200.zip",
                "sha256": "aggregate-sha",
                "size": 1,
                "source": "built-windows-native-aot",
                "commitHash": "a" * 40,
            },
            "bundleInstaller": {
                "fileName": "Installer_TrayAppDotNET.exe",
                "sha256": "bundle-sha",
                "size": 2,
            },
            "apps": [
                {
                    "appId": "BatteryTrayAppDotNET",
                    "version": 10,
                    "fileName": "BatteryTrayAppDotNET_10.zip",
                    "sha256": "app-sha",
                    "size": 3,
                    "source": "reused-TrayAppDotNET_100",
                    "commitHash": "b" * 40,
                    "installerFileName": "Installer_BatteryTrayAppDotNET.exe",
                    "installerSha256": "installer-sha",
                    "installerSize": 4,
                }
            ],
        }

        rows = PUBLISH.artifact_rows([manifest])

        self.assertEqual(
            ["aggregate", "app", "installer", "installer"],
            [row["kind"] for row in rows],
        )
        app_installer_row = rows[2]
        self.assertEqual("BatteryTrayAppDotNET", app_installer_row["appId"])
        self.assertEqual(10, app_installer_row["version"])
        self.assertEqual(
            "Installer_BatteryTrayAppDotNET.exe", app_installer_row["fileName"]
        )
        self.assertEqual("installer-sha", app_installer_row["sha256"])
        self.assertEqual(4, app_installer_row["size"])
        self.assertEqual("built-windows-native-aot", app_installer_row["source"])
        self.assertEqual("a" * 40, app_installer_row["commitHash"])
        bundle_installer_row = rows[3]
        self.assertEqual("TrayAppDotNET", bundle_installer_row["appId"])
        self.assertEqual(200, bundle_installer_row["version"])
        self.assertEqual(
            "Installer_TrayAppDotNET.exe", bundle_installer_row["fileName"]
        )
        self.assertEqual("bundle-sha", bundle_installer_row["sha256"])
        self.assertEqual(2, bundle_installer_row["size"])
        self.assertEqual("a" * 40, bundle_installer_row["commitHash"])

        with tempfile.TemporaryDirectory() as temporary_directory:
            versions_path = Path(temporary_directory) / "versions.xml"
            PUBLISH.write_versions_manifest(
                versions_path, rows, 200, "owner/repository", "TrayAppDotNET_200"
            )
            artifacts = (
                ET.parse(versions_path).getroot().findall("./artifacts/artifact")
            )

        self.assertEqual(
            ["aggregate", "app", "installer", "installer"],
            [artifact.get("kind") for artifact in artifacts],
        )
        self.assertEqual(
            ["Installer_BatteryTrayAppDotNET.exe", "Installer_TrayAppDotNET.exe"],
            [
                artifact.get("fileName")
                for artifact in artifacts
                if artifact.get("kind") == "installer"
            ],
        )

    def test_publish_release_uploads_app_and_bundle_installers(self) -> None:
        # Deliberately reversed so the bundle payload order proves the appId sort.
        first_app = PUBLISH.APPS[1]
        second_app = PUBLISH.APPS[0]
        arguments = SimpleNamespace(
            input_root="",
            output_root="",
            installer_factory="",
            profiles="release",
            apps=f"{first_app.name},{second_app.name}",
            repo="owner/repository",
            tray_version="200",
            release_tag="",
            target="HEAD",
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            input_root = Path(temporary_directory) / "collected"
            package_root = input_root / "release"
            for app, version in ((first_app, 10), (second_app, 7)):
                write_collected_app_manifest(package_root, app, version)
            arguments.input_root = str(input_root)
            output_root = Path(temporary_directory) / "publish"
            arguments.output_root = str(output_root)
            factory_path = (
                Path(temporary_directory) / PUBLISH.INSTALLER_FACTORY_EXECUTABLE_NAME
            )
            factory_path.write_bytes(FACTORY_BYTES)
            arguments.installer_factory = str(factory_path)
            final_dir = input_root / "_release"
            bundle_installer_path = final_dir / "Installer_TrayAppDotNET.exe"

            def fake_build_bundle_installer(*_positional_arguments) -> Path:
                bundle_installer_path.write_bytes(b"bundle")
                return bundle_installer_path

            with (
                mock.patch.object(PUBLISH, "latest_release", return_value=None),
                mock.patch.object(
                    PUBLISH, "try_resolve_git_commit", return_value="a" * 40
                ),
                mock.patch.object(
                    PUBLISH, "latest_reachable_release_tag", return_value=""
                ),
                mock.patch.object(PUBLISH, "commits_since_release", return_value=[]),
                mock.patch.object(
                    PUBLISH, "pull_requests_for_commits", return_value=[]
                ),
                mock.patch.object(
                    PUBLISH,
                    "build_bundle_installer",
                    side_effect=fake_build_bundle_installer,
                ) as build_bundle_installer,
                mock.patch.object(
                    PUBLISH, "build_installer_factory"
                ) as build_installer_factory,
                mock.patch.object(PUBLISH, "ensure_release"),
                mock.patch.object(
                    PUBLISH, "prune_release_assets"
                ) as prune_release_assets,
                mock.patch.object(PUBLISH, "run") as run,
            ):
                self.assertEqual(0, PUBLISH.publish_release(arguments))

            artifacts = (
                ET.parse(final_dir / "versions.xml")
                .getroot()
                .findall("./artifacts/artifact")
            )

        build_installer_factory.assert_not_called()
        build_bundle_installer.assert_called_once_with(
            factory_path,
            [
                package_root / f"{second_app.name}_7.zip",
                package_root / f"{first_app.name}_10.zip",
            ],
            final_dir,
        )
        keep_names = prune_release_assets.call_args.args[2]
        self.assertIn("Installer_TrayAppDotNET.exe", keep_names)
        self.assertIn(f"Installer_{first_app.name}.exe", keep_names)
        self.assertIn(f"Installer_{second_app.name}.exe", keep_names)
        upload_command = run.call_args.args[0]
        self.assertEqual(
            ["gh", "release", "upload", "TrayAppDotNET_200"], upload_command[:4]
        )
        self.assertIn(str(bundle_installer_path), upload_command)
        self.assertIn(
            str(package_root / f"Installer_{first_app.name}.exe"), upload_command
        )
        self.assertIn(
            str(package_root / f"Installer_{second_app.name}.exe"), upload_command
        )
        self.assertEqual(
            [
                "Installer_BatteryTrayAppDotNET.exe",
                "Installer_BrightnessTrayAppDotNET.exe",
                "Installer_TrayAppDotNET.exe",
            ],
            [
                artifact.get("fileName")
                for artifact in artifacts
                if artifact.get("kind") == "installer"
            ],
        )

    def test_native_aot_publish_requires_loose_native_dlls(self) -> None:
        app = PUBLISH.APPS[0]
        profile = PUBLISH.PROFILES["release"]

        with tempfile.TemporaryDirectory() as temporary_directory:
            publish_dir = Path(temporary_directory)
            write_fake_native_aot_publish(publish_dir, app)
            PUBLISH.validate_publish_dir(app, publish_dir, profile)

            (publish_dir / "libSkiaSharp.dll").unlink()
            with self.assertRaisesRegex(SystemExit, r"libSkiaSharp\.dll") as context:
                PUBLISH.validate_publish_dir(app, publish_dir, profile)

        self.assertNotIn("libHarfBuzzSharp.dll", str(context.exception))
        self.assertNotIn("av_libglesv2.dll", str(context.exception))


if __name__ == "__main__":
    unittest.main()
