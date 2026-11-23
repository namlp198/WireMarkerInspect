# Wire Marker Inspection

Read README.md, docs/00_CONTEXT.md, tasks/active.md, handoff/latest.md and docs/design before changes.
The user approved the implementation plan on 2026-08-29.
Windows x64, WPF/.NET 8, MVVM; processing core is C++ through a C ABI.
One camera captures two ends. Each end has one search ROI containing automatically detected ordered text regions.
OK requires exact text (punctuation, case and order) plus the configured per-end 0°/180° text direction. Never repair or choose OCR output from expected text or the other end.
Terminal template matching was approved on 2026-09-04: per-end Normal, AKAZE, SIFT, ORB and ORB Max Stable; combine its verdict with exact OCR/direction. Color classification and print-quality inspection remain out of scope.
Use shared design tokens; ImageViewer is read-only, ImageEditor adds geometry editing, recipe logic stays outside controls.
HUD style is mandatory: wrap image surfaces in ImageHud; follow RoboStation Geo Measure tool rail/overlay/icon styles. Never restore external text-button geometry or zoom toolbars. DESIGN_SYSTEM.md governs this requirement.
Current approved compact UI: metallic chevron SETTING/RUN taskbar at 208x64 with parallel tip/notch edges; 400-DIP camera and Model Library columns; model row is ComboBox + Add/Edit/Delete icons; recipe editor has no caption/helper labels; HUD button/icon 32/16 DIP at 53% alpha; use icons and tooltips for practical actions.
ChevronButton exposes TailMode: Straight for the first taskbar button, Notched (default) for following buttons. Do not fork the ModeButton style to change the tail.
Missing assets/hardware must be visible errors, never synthetic passing results.
Do not modify the reference repositories. Preserve source provenance.
Run managed/native tests and an offline WPF smoke. Hardware acceptance requires actual hardware.
Update context, task and handoff documents before finishing a coding session.

## Git workflow (bắt buộc — chuẩn tại 06_Repositories/_standards/GIT-WORKFLOW.md)

- Nhánh: `main` (trunk ổn định), `develop`, `feature/*`, `fix/*`, `release/x.y`, `hotfix/x.y-*`.
- **Chỉ làm việc trên `develop`, `feature/*`, `fix/*`, `hotfix/*`.** Tuyệt đối không commit, push, merge cục bộ hay tag trên `main`/`release/*`. Nếu đang đứng ở nhánh đó, chuyển sang `develop` trước khi sửa file.
- Tag SemVer `vX.Y.Z` (firmware: `att-vX.Y.Z`, `sff-vX.Y.Z`, `mff-vX.Y.Z`), chỉ trên `release/*`.
- Nhánh AI tạo phải đặt tên `feature/*`, không dùng `claude/*`, `codex/*`.
- Đầu phiên: chạy `pwsh/powershell ..\_standards\scripts\Get-RepoSyncStatus.ps1 -Repo <tên repo>`; nếu `develop` có commit từ ngày trước chưa vào `main`, nhắc người dùng.
- Cuối phiên (hoặc khi task xong): nếu `develop` đi trước `main` và đủ điều kiện (tree sạch, đã push, CI xanh, không xung đột) thì **hỏi** người dùng có merge `develop → main` (qua PR) không. Không tự merge khi chưa được đồng ý trong phiên đó.
