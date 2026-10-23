"""1530417 busy/dialog wiring regressions, not a substitute for Windows UI tests."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1] / 'src/Rhi.App'
def read(path): return (ROOT / path).read_text(encoding='utf-8-sig')

class BusyLifecycleSourceTests(unittest.TestCase):
    def test_observations_publish_both_busy_edges(self):
        source = read('Services/AppSession.Observations.cs')
        self.assertNotIn('if (refresh)', source)
        self.assertEqual(source.count('finally { Busy = false; Changed?.Invoke(); }'), 2)
        for name in ('InspectTargets', 'InspectProtection'):
            body = source.split('public async Task', 1)[1] if name == 'InspectTargets' else source.split('public async Task InspectProtection', 1)[1]
            self.assertLess(body.index('try'), body.index('Busy = true'))

    def test_snapshot_matches_game_and_revision(self):
        source = read('Pages/Games/Inspection.cs')
        self.assertNotIn('refresh: false', source)
        self.assertIn('observation?.GameId != game.Id', source)
        self.assertIn('observation.Revision != c.Session.State!.Revision', source)
        self.assertIn('Strings.Format("LastChecked", observation.CheckedAt.ToLocalTime())', source)
        self.assertIn('Ui.FileDetails(', source)  # Keep 1530417 lazy virtualization.
        self.assertNotIn('foreach (var file in current.Files)', source)

    def test_queued_button_clicks_do_not_open_error_dialogs(self):
        source = read('Presentation/PageContext.cs')
        self.assertIn('if (running || Session.Busy) return;', source)
        self.assertIn('finally { running = false; }', source)

    def test_all_dialog_routes_share_one_released_gate(self):
        source = read('Presentation/PageContext.cs')
        self.assertIn('await dialogGate.WaitAsync();', source)
        self.assertIn('finally { dialogGate.Release(); }', source)
        self.assertEqual(source.count('await dialog.ShowAsync()'), 1)
        for path in ('Presentation/Actions/ChoiceDialog.cs', 'Presentation/Forms/ValidatedForm.cs'):
            self.assertIn('await PresentDialog(dialog)', read(path))
            self.assertNotIn('await dialog.ShowAsync()', read(path))

if __name__ == '__main__': unittest.main()
