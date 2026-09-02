# Wire Marker Inspection — DESIGN SYSTEM

## 1. Phong cách bắt buộc: Image-centric HUD theo RoboStation

Cập nhật theo yêu cầu trực tiếp của người dùng ngày 2026-08-29. **Toàn bộ control ảnh của phần mềm phải dùng HUD theo RoboStation / Geo Measure.** Đây là quy chuẩn triển khai, không phải một theme tùy chọn.

Ảnh là bề mặt chính. Công cụ nổi trực tiếp trên ảnh trong các thanh nền navy bán trong suốt; icon nét mảnh, ít viền, trạng thái chọn màu xanh. Giữ ảnh gốc, không phủ tint hoặc giảm opacity ảnh để tạo cảm giác HUD. Opacity chỉ thuộc nền thanh công cụ; icon và chữ luôn rõ.

Nguồn đối chiếu:
- `design/references/hud-viewer.png`: toolbar ngang nổi ở góc phải dưới.
- `design/references/hud-editor.png`: rail công cụ dọc nổi bên trái.
- `design/references/taskbar-chevron.png`: taskbar kim loại dạng chevron.
- `design/references/taskbar-chevron-parallel.png`: quan hệ hình học bắt buộc giữa mũi nút trước và hõm nút sau.
- `design/references/hud-cleanup-annotation.png` và `setting-layout-annotation.png`: các thành phần cần bỏ, tỉ lệ panel và mức thu gọn HUD đã được người dùng duyệt bằng chỉ dẫn trực quan.
- `design/references/status-apply-annotation.png`: trạng thái cần chú ý phải nổi bật và Apply phải là nút chữ.
- RoboStation `src/RoboStation.Desktop/Views/GeoMeasure/GeoMeasureView.xaml`.
- RoboStation `Themes/Components/Buttons.xaml`, `Overlay.xaml`, `Themes/Tokens/ColorsOverlay.xaml`, `Icons.xaml`, `Sizing.xaml`.

Chỉ thích ứng ngôn ngữ giao diện và control; không mang bản đồ, RTK, robot hay menu của RoboStation vào ứng dụng kiểm tra dây. Các resource đã sao chép có provenance và thuộc repository này, không load từ repository tham khảo khi chạy.

## 2. Hình học và vị trí HUD

| Thành phần | Quy định |
|---|---|
| Canvas ảnh | Chiếm toàn bộ vùng control, giữ tỉ lệ, Fit có letterbox; HUD không chiếm một hàng/cột layout của ảnh |
| Rail ImageEditor | Dọc, phía trái, căn giữa; Select, Rectangle, Circle, Polygon, Pan; separator; Undo, Redo, Delete |
| Toolbar điều hướng | Ngang, góc phải dưới; chỉ Zoom out, Zoom in và Reset về Fit mặc định lúc load ảnh |
| Caption | Chỉ dùng khi màn hình cần metadata; ImageEditor recipe không hiện caption |
| Expand | Icon nổi ở góc phải trên của editor; mở lại cùng recipe trong cửa sổ lớn |
| Polygon | Thanh nổi cạnh rail khi có điểm đang vẽ: Finish, Undo point, Cancel; Finish chỉ bật từ 3 điểm |
| Verdict RUN | Badge nổi góc phải trên; luôn có chữ OK / NG / ERROR / CHỜ ẢNH |

Kích thước dùng DIP: nút 32×32 (`Hud.ButtonSize`), icon 16 (`Hud.IconSize`), nét 1.5, rail rộng 44 (`Layout.ToolRailWidth`), padding rail 5 (`Hud.RailPadding`), inset 8 (`Hud.Inset`). Canvas editor tối thiểu 420 (`Hud.EditorMinHeight`).

Thanh chỉ có một nền chung; nút bình thường trong suốt. Không viền riêng từng icon. Corner radius dùng `Radius.SM` cho nút và `Radius.MD` cho container. Shadow dùng `Elevation.Overlay`, không thêm glow.

## 3. Tokens màu

| Vai trò | Token | Giá trị WPF ARGB / RGB |
|---|---|---|
| Nền ứng dụng | Brush.Background.App | #0B1220 |
| Form / bảng dữ liệu | Brush.Background.Surface | #111A2B |
| Nền HUD | Brush.Overlay.Widget | #88111A2B (53% alpha) |
| Panel HUD | Brush.Overlay.Panel | #88111A2B |
| Viền HUD | Brush.Overlay.PanelBorder | #663B4A63 |
| Chữ chính | Brush.Text.Primary | #F3F6FA |
| Icon / chữ phụ | Brush.Text.Secondary | #AAB5C5 |
| Công cụ đang chọn | Brush.Brand.Primary | #2F80ED |
| Nền công cụ chọn | Brush.Background.Selected | Theo Colors.xaml của RoboStation |
| OK | Brush.Status.Success | #2DBE78 |
| NG | Brush.Status.Error | #E45858 |
| Cảnh báo / lỗi xử lý | Brush.Status.Warning | #F2B84B |

Các dòng trạng thái yêu cầu người vận hành hành động, ví dụ `Có thay đổi · cần Apply`, `Load ảnh mẫu để bắt đầu` hoặc `Cần lưu recipe trước khi RUN`, dùng `StatusMessage.Attention`: chữ warning, semibold, không hiển thị như caption mờ. Khi một đầu dây đã Apply hợp lệ, thông báo của đầu đó chuyển sang `Brush.Status.Success`. Không dùng màu trạng thái cho nhãn mô tả tĩnh.

Dùng chung geometry icon của RoboStation. Các icon bổ sung Select, Pan, Redo, Expand, ActualSize, Check, Close giữ lưới authoring 18×18, stroke tròn 1.5, không fill. Không dùng emoji, Unicode glyph hoặc ảnh bitmap làm icon công cụ.

## 4. Trạng thái và tương tác

- Default: nền nút trong suốt, icon màu secondary.
- Hover: `Brush.Background.Hover`, icon primary.
- Selected: `Brush.Background.Selected`, icon brand blue; chỉ một công cụ vẽ/Pan được chọn.
- Pressed: nền selected. Keyboard focus: viền xanh rõ. Disabled: opacity 0.35 và thực sự chặn thao tác.
- Mỗi icon phải có tooltip mô tả / phím tắt và tên accessibility.
- Trạng thái cập nhật khi click icon, dùng phím tắt, commit, undo/redo hoặc thay ảnh. Không để nút Polygon vẫn xanh sau khi đã chuyển về Select.
- ImageViewer runtime chỉ có điều hướng zoom, tuyệt đối không có rail chỉnh ROI.
- Nút cuối navigation (icon `[1]`) reset viewport về Fit mặc định lúc load ảnh, gồm cả zoom và offset căn giữa. Không dùng nút này cho 100%/1:1 pixel-to-DIP.
- Save Recipe ở trạng thái clean phải disabled/nhạt. Khi recipe dirty, icon Save sáng, hiện đúng một notify đỏ `● CẦN LƯU` cạnh nút; notify biến mất ngay sau khi lưu thành công.
- RUN dùng warning nổi bật cho `CHỜ ĐẦU 1/2`; Stop luôn có viền error đỏ khi RUN hoạt động.
- OK/NG của từng đầu và verdict tổng dùng 40 DIP (gấp đôi kích thước cũ 20 DIP). Text đọc được và detail màu success khi OK, màu error khi NG/ERROR.
- Không tự tạo OK, OCR text hoặc camera frame để làm đầy giao diện. Nguồn offline/synthetic phải ghi rõ.

## 5. Phân tách control và nghiệp vụ

`WireMarkerInspection.Controls.ImageViewer`: rendering, transform, zoom/pan, overlay read-only.
`ImageEditor`: kế thừa viewport, thêm một ROI editable và undo history.
`ImageHud`: chrome dùng chung cho cả hai loại control; tự hiện rail khi Viewer là ImageEditor. Resource được resolve từ theme của ứng dụng. Không tham chiếu Desktop, camera, recipe store hay OCR service.

`EndEditorView`: ghép HUD editor với text mẫu, orientation, Load Image, Grab Image, Test OCR và Apply ở ngoài canvas. Checkbox OCR/Color/Template nằm trong form ứng dụng, không thuộc control mẫu. Nút nghiệp vụ được phép có chữ; chỉ toolbar hình học/viewport phải là HUD icon. Riêng Apply là hành động commit draft nên bắt buộc dùng button chữ `Button.Apply` với nền primary nổi bật; không thay bằng icon dấu kiểm. Quy tắc này áp dụng cho cả thông số camera và từng đầu dây.

Cấm: dãy nút chữ Rectangle/Circle/Polygon/Pan/Fit bên trên hoặc dưới ảnh; khung card lồng nhiều lớp/padding lớn quanh canvas; đặt thanh vẽ vào form ngoài ảnh; nhân bản renderer hay HUD riêng cho từng màn hình.

## 6. Bố cục ứng dụng

Giữ hai mode SETTING / RUN.

Taskbar SETTING/RUN dùng chevron kim loại 208×64 DIP, icon 18 và nhãn ngắn. Mỗi nút rộng 208, overlap layout 30 DIP; cạnh mũi của nút trước và cạnh hõm của nút sau dùng cùng vector 42×31, cách nhau 14 DIP và vì vậy luôn song song. Nút sau vẽ trên nút trước để hõm không bị mũi che. Active có viền cyan và gradient sáng; inactive dùng gradient bạc. Không dùng lại hai nút chữ hình chữ nhật của phiên bản đầu.

`ChevronButton.TailMode` là property bắt buộc để chọn hình đuôi:
- `Straight`: cạnh trái thẳng, dùng cho button đầu chuỗi.
- `Notched`: đuôi hõm chevron, là mặc định và dùng cho các button phía sau.

Không xác định hình dạng bằng thứ tự visual tree hoặc converter. XAML phải khai báo `TailMode="Straight"` rõ ràng cho nút đầu để control vẫn tái sử dụng được khi thêm mode.

SETTING: camera settings và Live Camera ở cột trái; model actions ở trên hai editor trung tâm; model library ở phải. Hai cột bên rộng 400 DIP, chủ động thu phần giữa. Badge vai trò và Login/Logout nằm trong card USER ACCESS riêng phía trên ACQUISITION; ACQUISITION không chứa thêm bộ chọn model. Operator chỉ được dùng ComboBox chọn model trong callout trung tâm; Add/Edit/Delete, cả hai editor, Save Recipe và Model Library bên phải đều disabled. Chỉ các vùng camera parameters và PLC/trigger/output dành cho Admin ở panel trái bị hidden. Hàng chọn model là callout viền nổi bật, luôn cho thấy code/name/revision hoặc cảnh báo chưa chọn, sau đó là combobox + Add/Edit/Delete; code/name được sửa trong dialog, Save Recipe là icon ở Model Library. RUN có callout riêng cho recipe đã freeze. Chuyển Live Camera sang cột acquisition để dành chiều cao cho hai canvas và rail HUD.

Header chỉ hiển thị tên sản phẩm, bộ chọn ngôn ngữ và trạng thái camera. `OFFLINE` dùng status-error, `ONLINE` dùng status-success; không hiển thị chi tiết công nghệ triển khai. Toàn bộ text hướng dẫn/thao tác phải đi qua catalog Việt/Anh/Hàn, ngoại trừ tên giao thức, hãng và đơn vị kỹ thuật.

RUN: hai canvas read-only ngang nhau; caption/verdict/toolbar nổi trên ảnh; crop OCR và text mẫu/đọc được bên dưới. Không dùng màu xanh cho trạng thái chưa kiểm tra.

Mục tiêu render chính 1920×1080. Cửa sổ tối thiểu rộng 1366 DIP; ở 1366×900 các form/editor cuộn độc lập, không thu nhỏ hit target hay cắt rail. Expand dùng cho chỉnh ROI chính xác. Kiểm thử DPI / chuột trên máy thật vẫn là bước nghiệm thu riêng.

## 7. Resource và nghiệm thu

Segoe UI cho form, Cascadia Mono/Consolas cho text code. Body 12–15, heading 16–24, spacing theo bội số 4. Form giữ style Inputs/Buttons/DataGrid của RoboStation.

`Themes/Tokens/_Tokens.xaml` tổng hợp token; `Themes/Components` định nghĩa style; `DarkTheme.xaml` lắp theme. Dictionary có StaticResource/BasedOn phải tự import dependency. MainWindow khai báo background/foreground tường minh.

Sau thay đổi HUD phải build, test trạng thái/control, chạy WPF smoke thật và xem ảnh SETTING/RUN/expanded HUD. Smoke kiểm tra bounds và va chạm giữa rail, caption, toolbar ở hai kích thước. Fixture minh họa không phải ảnh test OCR. Light theme và animation chưa nằm trong phạm vi hiện tại.
