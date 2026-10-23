"""Portable wiring checks only; filesystem behavior is covered by ProtectionRecordTests in Windows CI."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ProtectionRecordWiring(unittest.TestCase):
    def test_confirmation_is_cancel_by_default_and_result_reaches_record_only_api(self):
        source = (ROOT / "src/Rhi.App/Pages/Settings/ProtectionPage.cs").read_text()
        self.assertIn('var confirmed = await c.Confirm("RemoveProtectionRecord"', source)
        self.assertIn('Ui.Label("RemoveProtectionRecordConfirm")', source)
        self.assertIn('dialog.DefaultButton = ContentDialogButton.Primary', source)
        self.assertIn('RemoveProtectionRecord(copy.Entry.OperationId, confirmed)', source)
        # PageContext maps Primary to Cancel; only Secondary is confirmation.
        context = (ROOT / "src/Rhi.App/Presentation/PageContext.cs").read_text()
        self.assertIn('PrimaryButtonText = Strings.Get("Cancel")', context)
        self.assertIn('== ContentDialogResult.Secondary', context)

    def test_refresh_has_one_guarded_callback(self):
        source = (ROOT / "src/Rhi.App/Pages/Settings/ProtectionPage.cs").read_text()
        self.assertIn('c.Button("RefreshProtection", RefreshInventory, !c.Session.Busy)', source)
        self.assertNotIn('refresh.Click +=', source)

    def test_removal_api_writes_only_workspace_ignore_metadata(self):
        source = (ROOT / "src/Rhi.Core/Storage/ProtectionCatalog.cs").read_text()
        removal = source.split('public static bool RemoveRecord', 1)[1].split('public static ProtectionInventory Read', 1)[0]
        self.assertIn('if (!confirmed) return false;', removal)
        self.assertIn('store.Save(store.State with { HiddenProtectionIds', removal)
        for forbidden in ('File.Delete', 'Directory.Delete', 'File.Move', 'Directory.Move', 'File.Write', 'AtomicJson.Write'):
            self.assertNotIn(forbidden, removal)
        self.assertIn('Where(x => !store.State.HiddenProtectionIds.Contains(x.Entry.OperationId))', source)


if __name__ == '__main__':
    unittest.main()
