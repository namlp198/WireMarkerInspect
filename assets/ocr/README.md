# OCR runtime assets
Place the actual validated model files here:
- detector.onnx: PaddleOCR DB detector, float input [1,3,H,W], probability output [1,1,H,W].
- recognizer.onnx: English CTC recognizer, input [1,3,48,320], probability output [1,T,C].
- dictionary.txt: UTF-8, one character/token per line in exact training order, excluding CTC blank.
The native engine allows the common extra trailing space class.
Missing/incompatible files block OCR. No mock OCR is substituted.
Model licensing, provenance and accuracy must be recorded when supplied.
