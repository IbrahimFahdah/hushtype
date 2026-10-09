# Model attribution

Hushtype downloads ONNX conversions of **NVIDIA Parakeet TDT 0.6B v2** for local English speech recognition.

- Original model and creator: NVIDIA, https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2
- Original model license: Creative Commons Attribution 4.0 International, https://creativecommons.org/licenses/by/4.0/
- ONNX conversion publisher: Fangjun Kuang (csukuangfj), sherpa-onnx project.
- Full-precision conversion: https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2 at revision 86891485dd8ad7cb28cb1aade45c3e23d0197c30.
- INT8 conversion: https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8 at revision 1ab9323565ddb038682214b292f588070a538ce2.

The downloaded files have been converted to ONNX; the INT8 profile additionally applies quantization. Hushtype does not retrain or otherwise modify the model files. The default uses the INT8 conversion. No endorsement by NVIDIA or the conversion publisher is implied.

The complete pinned file list, byte sizes and SHA-256 hashes are in src/Hushtype.Speech/models.json. The application downloads those revisions and verifies every file before loading it. Retain this attribution when distributing the app with these models or repackaging their artifacts.

The sherpa-onnx runtime is separately licensed under Apache 2.0: https://github.com/k2-fsa/sherpa-onnx/blob/master/LICENSE.
