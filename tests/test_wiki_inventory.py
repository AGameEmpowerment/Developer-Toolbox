#!/usr/bin/env python3
"""Regression tests for the GitHub wiki inventory safety boundaries."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT_PATH = (
    Path(__file__).resolve().parents[1]
    / ".github"
    / "skills"
    / "github-wiki"
    / "scripts"
    / "wiki_inventory.py"
)
SPEC = importlib.util.spec_from_file_location("wiki_inventory", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {SCRIPT_PATH}")
WIKI_INVENTORY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(WIKI_INVENTORY)


class PrepareWikiSafetyTests(unittest.TestCase):
    def make_info(self, root: Path, *, enabled: bool = True) -> dict:
        return {
            "root": str(root),
            "wiki_enabled": enabled,
            "wiki_git_url": "https://github.com/example/project.wiki.git",
            "wiki_web_url": "https://github.com/example/project/wiki",
        }

    def test_rejects_wiki_directory_inside_project_before_running_git(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir) / "project"
            root.mkdir()

            with patch.object(WIKI_INVENTORY, "run") as run_mock:
                result = WIKI_INVENTORY.prepare_wiki(
                    self.make_info(root), str(root / "wiki"), no_clone=False
                )

            self.assertTrue(result["error"])
            self.assertEqual("invalid wiki directory", result["status"])
            run_mock.assert_not_called()

    def test_rejects_existing_clone_with_different_remote_before_pull(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            wiki_dir = base / "project.wiki"
            root.mkdir()
            (wiki_dir / ".git").mkdir(parents=True)

            with patch.object(
                WIKI_INVENTORY,
                "run",
                return_value=(0, "https://github.com/example/other.wiki.git"),
            ) as run_mock:
                result = WIKI_INVENTORY.prepare_wiki(
                    self.make_info(root), str(wiki_dir), no_clone=False
                )

            self.assertTrue(result["error"])
            self.assertEqual("existing clone remote mismatch", result["status"])
            run_mock.assert_called_once_with(
                ["git", "remote", "get-url", "origin"], str(wiki_dir)
            )

    def test_disabled_wiki_stops_before_running_git(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            root.mkdir()

            with patch.object(WIKI_INVENTORY, "run") as run_mock:
                result = WIKI_INVENTORY.prepare_wiki(
                    self.make_info(root, enabled=False),
                    str(base / "project.wiki"),
                    no_clone=False,
                )

            self.assertTrue(result["error"])
            self.assertEqual("wiki disabled", result["status"])
            run_mock.assert_not_called()

    def test_missing_wiki_with_no_clone_is_an_error(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            root.mkdir()

            result = WIKI_INVENTORY.prepare_wiki(
                self.make_info(root), str(base / "project.wiki"), no_clone=True
            )

            self.assertTrue(result["error"])
            self.assertEqual("missing (clone skipped)", result["status"])

    def test_unknown_wiki_url_is_an_error(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            root.mkdir()
            info = self.make_info(root)
            info["wiki_git_url"] = None

            result = WIKI_INVENTORY.prepare_wiki(
                info, str(base / "project.wiki"), no_clone=False
            )

            self.assertTrue(result["error"])
            self.assertEqual("unknown wiki URL", result["status"])

    def test_clone_failure_is_an_error(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            root.mkdir()

            with patch.object(
                WIKI_INVENTORY, "run", return_value=(1, "fatal: repository not found")
            ):
                result = WIKI_INVENTORY.prepare_wiki(
                    self.make_info(root), str(base / "project.wiki"), no_clone=False
                )

            self.assertTrue(result["error"])
            self.assertEqual("clone failed: fatal: repository not found", result["status"])

    def test_pull_failure_is_an_error(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            root = base / "project"
            wiki_dir = base / "project.wiki"
            root.mkdir()
            (wiki_dir / ".git").mkdir(parents=True)

            with patch.object(
                WIKI_INVENTORY,
                "run",
                side_effect=[
                    (0, "https://github.com/example/project.wiki.git"),
                    (1, "fatal: not possible to fast-forward"),
                ],
            ):
                result = WIKI_INVENTORY.prepare_wiki(
                    self.make_info(root), str(wiki_dir), no_clone=False
                )

            self.assertTrue(result["error"])
            self.assertEqual("pull failed: fatal: not possible to fast-forward", result["status"])

    def test_ssh_and_https_remotes_for_same_wiki_are_equivalent(self) -> None:
        https_remote = "https://github.com/example/project.wiki.git"
        ssh_remote = "git@github.com:example/project.wiki.git"

        self.assertEqual(
            WIKI_INVENTORY.normalize_git_remote(https_remote),
            WIKI_INVENTORY.normalize_git_remote(ssh_remote),
        )

    def test_render_text_uses_explicit_default_depth_without_cli_state(self) -> None:
        info = {
            "root": "project",
            "name_with_owner": "example/project",
            "url": "https://github.com/example/project",
            "description": None,
            "branch": "main",
            "head": "abc1234",
            "default_branch": "main",
            "uncommitted_changes": 0,
            "behind_remote": 0,
            "wiki_enabled": True,
            "wiki_web_url": "https://github.com/example/project/wiki",
            "wiki_git_url": "https://github.com/example/project.wiki.git",
            "warnings": [],
            "tracked_files": 0,
            "tree": {},
            "doc_files": [],
            "extensions": [],
            "recent_commits": [],
        }
        wiki = {
            "dir": "project.wiki",
            "status": "existing clone (not refreshed)",
            "warnings": [],
            "pages": [],
        }

        output = WIKI_INVENTORY.render_text(info, wiki)

        self.assertIn("== Project tree (depth 3, 0 tracked files) ==", output)


if __name__ == "__main__":
    unittest.main()

