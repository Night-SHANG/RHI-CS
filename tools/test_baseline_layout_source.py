"""Source guards only; native geometry/click regression is in ci_ui_baseline_layout.ps1."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[1]
class LayoutRegression(unittest.TestCase):
    def test_form_is_content_sized_and_preview_retains_stable_height(self):
        s = (ROOT / 'src/Rhi.App/Presentation/Forms/DialogSizing.cs').read_text()
        self.assertNotIn('preferredHeight', s)
        self.assertIn('else if (stableContent)', s)
    def test_empty_validation_collapses(self):
        s = (ROOT / 'src/Rhi.App/Presentation/Forms/ValidatedForm.cs').read_text()
        self.assertNotIn('error.MinHeight = 58', s)
        self.assertNotRegex(s, r'(?<!Max)Height = 66,')
        self.assertIn('Visibility.Collapsed', s)
    def test_fifth_action_fits_when_width_allows(self):
        s = (ROOT / 'src/Rhi.App/Presentation/Actions/UiActions.cs').read_text()
        self.assertNotIn('MaximumRowsOrColumns = 4', s)
        self.assertIn('Math.Floor', s)
        s = (ROOT / 'src/Rhi.App/Pages/LibrariesView.cs').read_text()
        self.assertIn('"LibraryOpenFolder"), relocate)', s)
if __name__ == '__main__': unittest.main()
