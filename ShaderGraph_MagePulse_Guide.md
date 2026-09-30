# Mage Pulse Shader Graph

Hiệu ứng này dành cho đạn phép mặc định của Mage trong Hero-Doom.

## Vị trí
- Shader Graph: `Assets/Project_/Resources/Shaders/MagePulse.shadergraph`
- Material: `Assets/Project_/Resources/Materials/MagePulse.mat`
- Script sử dụng: `Assets/Project_/Resources/Scripts/MagicProjectile.cs`

## Luồng node trực quan
1. `Time -> Multiply(Pulse Speed) -> Sine -> Add(1) -> Multiply(0.5)`
   - Biến giá trị sin từ -1..1 thành nhịp 0..1 để tạo hiệu ứng sáng/tối liên tục.
2. `Position (Object) + View Direction + Fresnel Power -> Fresnel Effect`
   - Tạo viền sáng quanh quả cầu phép.
3. `Fresnel * Pulse -> Intensity`
   - Viền phép vừa có Fresnel vừa nhấp nháy theo thời gian.
4. `Mage Color * Intensity -> Base Color`
5. `Intensity -> Alpha`

## Property có thể chỉnh trong Material
- Mage Color: màu phép HDR.
- Pulse Speed: tốc độ nhấp nháy/chuyển động ánh sáng.
- Fresnel Power: độ dày và độ gắt của viền phép.

Khi Mage không có mageProjectilePrefab riêng, MagicProjectile sẽ tạo Sphere runtime và tự lấy material `Resources/Materials/MagePulse`.
