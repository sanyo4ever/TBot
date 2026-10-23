import importlib.util
import pathlib
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "release_version.py"
SPEC = importlib.util.spec_from_file_location("release_version", SCRIPT)
versioning = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(versioning)


class ReleaseVersionTests(unittest.TestCase):
    def test_author_version_and_letter_are_user_facing_label_and_tag(self):
        result = versioning.derive_version("3.4.6", "a", requested="3.4.6a")
        self.assertEqual(result, {"label": "3.4.6a", "version": "3.4.6-a",
                                  "assembly": "3.4.6", "tag": "3.4.6a"})

    def test_next_letter_retains_author_version(self):
        self.assertEqual(versioning.derive_version("3.4.6", "b")["label"], "3.4.6b")

    def test_new_author_release_can_restart_at_a(self):
        self.assertEqual(versioning.derive_version("3.4.7", "a")["label"], "3.4.7a")

    def test_ci_build_is_identifiable_without_reserving_a_release_tag(self):
        result = versioning.derive_version("3.4.6", "a", ci_run="42")
        self.assertEqual(result["label"], "3.4.6a-ci.42")
        self.assertEqual(result["version"], "3.4.6-a.ci.42")
        self.assertEqual(result["assembly"], "3.4.6")
        self.assertEqual(result["tag"], "")

    def test_request_cannot_publish_an_unreviewed_author_version_or_letter(self):
        for requested in ("3.4.7a", "3.4.6b", "v3.4.6a", "3.4.6-a", "3.4.6a\nassembly=9.9.9", ""):
            with self.subTest(requested=requested), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", "a", requested=requested)

    def test_invalid_author_versions_fail(self):
        for upstream in ("3.4", "v3.4.6", "03.4.6", "3.4.6-beta", "3.4.6\n"):
            with self.subTest(upstream=upstream), self.assertRaises(ValueError):
                versioning.derive_version(upstream, "a")

    def test_revision_is_one_lowercase_letter(self):
        for revision in ("", "aa", "A", "1", "a\n"):
            with self.subTest(revision=revision), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", revision)

    def test_invalid_ci_runs_fail(self):
        for run in ("0", "-1", "1.2", "x", "1\n"):
            with self.subTest(run=run), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", "a", ci_run=run)

    def test_release_and_ci_modes_cannot_be_mixed(self):
        with self.assertRaises(ValueError):
            versioning.derive_version("3.4.6", "a", requested="3.4.6a", ci_run="42")


if __name__ == "__main__":
    unittest.main()
