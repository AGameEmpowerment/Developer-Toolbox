"""Validate lifecycle preflight rules without starting a container runtime."""

import unittest
import subprocess
from unittest.mock import patch

from test_container_lifecycle import prepare_image, validate_restart_policies, verify


class RestartPolicyTests(unittest.TestCase):
    def test_image_preparation_delegates_only_selected_image_to_shared_policy(self):
        with patch("test_container_lifecycle.subprocess.run", return_value=subprocess.CompletedProcess([], 0)) as engine:
            prepare_image("docker", "docker.io/library/redis:latest")
        command = engine.call_args.args[0]
        self.assertIn("redis:latest", " ".join(command))
        self.assertIn("ensure_image.", " ".join(command))
        self.assertNotIn("docker_pull", " ".join(command))
        self.assertEqual(engine.call_args.kwargs["timeout"], 120)

    def test_image_preparation_rejects_unsafe_input_before_running_a_command(self):
        with patch("test_container_lifecycle.subprocess.run") as engine, self.assertRaises(ValueError):
            prepare_image("docker", "redis:latest';exit 0")
        engine.assert_not_called()

    def test_image_preparation_failure_is_reported(self):
        with patch("test_container_lifecycle.subprocess.run", return_value=subprocess.CompletedProcess([], 1)), \
             self.assertRaisesRegex(RuntimeError, "could not be prepared"):
            prepare_image("podman", "docker.io/library/redis:latest")

    def test_all_initialization_jobs_complete_without_automatic_restarts(self):
        validate_restart_policies({"services": {
            "aws-init": {"restart": "no"},
            "dynamodb-volume-init": {"restart": "no"},
            "redis": {"restart": "unless-stopped"},
            "aws-emulator": {"restart": "unless-stopped"},
        }})

    def test_a_persistent_service_that_restarts_after_manual_stops_is_rejected(self):
        for policy in ("always", "on-failure", "no", None):
            with self.subTest(policy=policy), self.assertRaisesRegex(AssertionError, "redis"):
                validate_restart_policies({"services": {"redis": {"restart": policy}}})

    def test_initializers_cannot_silently_gain_a_persistent_restart_policy(self):
        for name in ("aws-init", "dynamodb-volume-init"):
            with self.subTest(name=name), self.assertRaisesRegex(AssertionError, name):
                validate_restart_policies({"services": {name: {"restart": "unless-stopped"}}})

    def test_cloud_policy_failure_stops_lifecycle_check_before_container_start(self):
        for service in ("mssql", "aws-emulator"):
            with self.subTest(service=service):
                with patch("test_container_lifecycle.shared_definition", return_value={"services": {
                    "redis": {"restart": "unless-stopped"}, service: {"restart": "always"},
                }}) as definition, patch("test_container_lifecycle.run") as engine:
                    with self.assertRaisesRegex(AssertionError, service):
                        verify("docker")
                definition.assert_called_once_with("docker")
                engine.assert_not_called()


if __name__ == "__main__":
    unittest.main()
