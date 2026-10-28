# Componentes de terceros del proveedor facial local (OpenCV)

- **Ámbito:** proveedor `SCIPH-FaceOpenCV` ([ADR-012](../adr/ADR-012-face-verification.md), PR 2/2): paquetes NuGet
  OpenCvSharp5, runtimes nativos, modelos ONNX y la muestra de prueba Face Synthetics.
- **Fecha de la revisión:** 2026-10-28.
- **Naturaleza:** inventario de **hechos verificables de upstream** (metadatos de paquetes, archivos de licencia, scripts y
  workflows de compilación en el commit exacto, cadenas del binario). **No es una opinión legal** ni una conclusión sobre
  cumplimiento; las obligaciones de redistribución deben confirmarse con asesoría legal.
- **Estado del gate de licencias (D2/D34/D41): BLOQUEADO.** Ver [Puntos abiertos](#puntos-abiertos). Mientras siga
  bloqueado, la imagen Docker del Web BFF con el runtime OpenCV **no debe distribuirse fuera del equipo**.

## Versiones fijadas

| Artefacto | Versión exacta | Origen del binario |
|---|---|---|
| `OpenCvSharp5` (managed) | 5.0.0.20261003 | nuget.org |
| `OpenCvSharp5.runtime.win` | 5.0.0.20261003 | nuget.org (`OpenCvSharpExtern.dll`, `opencv_videoio_ffmpeg500_64.dll`) |
| `OpenCvSharp5.official.runtime.linux-x64.headless` | 5.0.0.20261003 | nuget.org (`libOpenCvSharpExtern.so`, ~140 MB, estático) |
| Commit fuente de los tres paquetes | `d8fc8914e09b43fe6e2ec71c1e376aef5188acf1` | `<repository commit=…>` de los tres `.nuspec` |
| OpenCV | 5.0.0 | `OPENCV_VERSION: 5.0.0` en `.github/workflows/manylinux.yml` @ `d8fc891`; `Cv2.GetVersionString()` = 5.0.0 |
| YuNet | `face_detection_yunet_2023mar.onnx`, SHA-256 `8f2383e4…52fa4` | Hugging Face `opencv/face_detection_yunet` |
| SFace | `face_recognition_sface_2021dec.onnx`, SHA-256 `0ba9fbfa…e79` | Hugging Face `opencv/face_recognition_sface` |

Los paquetes NuGet declaran `PackageLicenseExpression` **Apache-2.0** y **no contienen** ningún archivo `LICENSE`,
`NOTICE` ni `THIRD-PARTY-NOTICES` (solo `README.*.md`, `opencvsharp.png` y los binarios). El README del runtime dice:
"The full (non-slim) package includes FFmpeg (LGPL v2.1) and Tesseract statically linked"; el headless tiene los mismos
módulos sin `highgui` (GTK).

## Inventario

Leyenda: **Redistribuido** = viaja en la salida de `dotnet publish` del Web BFF y por tanto en su imagen Docker.
**Aviso incluido** = texto en `docs/legal/licenses/` copiado a `/app/licenses/` de la imagen.

| Componente | Fuente verificable | Licencia (según upstream) | Uso en SCI-PH | Redistribuido | Aviso incluido |
|---|---|---|---|---|---|
| OpenCvSharp (managed + `OpenCvSharpExtern`) | github.com/shimat/opencvsharp @ `d8fc891`, `LICENSE` | Apache-2.0 (Copyright 2008 shimat) | Binding .NET de OpenCV | Sí | Sí (`OpenCvSharp-LICENSE.txt`) |
| OpenCV 5.0.0 + módulos contrib | github.com/opencv/opencv @ 5.0.0, `LICENSE`; workflow usa `opencv_contrib/modules` | Apache-2.0 | `dnn`, `objdetect` (`FaceDetectorYN`, `FaceRecognizerSF`), `imgcodecs` | Sí (estático en `libOpenCvSharpExtern.so` / `OpenCvSharpExtern.dll`) | Sí (`Apache-2.0.txt`) |
| FFmpeg 7.1.1 (`avcodec` 61.19.101, `avformat` 61.7.100, `avutil` 59.39.100, `swscale` 8.3.100) | `packaging/docker/manylinux/build_static_deps.sh` @ `d8fc891`: tarball `ffmpeg.org/releases/ffmpeg-7.1.1.tar.xz`; `cv2.getBuildInformation()` | LGPL-2.1+ (configure **sin** `--enable-gpl` ni `--enable-nonfree`; cadena del binario "libswscale license: LGPL version 2.1 or later") | **Ninguno** (SCI-PH no usa `videoio`); viene dentro del binario nativo | Sí, **enlazado estáticamente** en Linux | Texto de licencia sí (`FFmpeg-COPYING.LGPLv2.1.txt`); fuente correspondiente: **no** (ver puntos abiertos) |
| FFmpeg (plugin Windows `opencv_videoio_ffmpeg500_64.dll`) | paquete `runtime.win`; cadenas `Lavc61.19.100`, "license: LGPL version 2.1 or later" | LGPL-2.1+ (sin `--enable-gpl`/`--enable-nonfree` en cadenas) | Ninguno; solo desarrollo local en Windows | Solo en un publish Windows (no en la imagen Docker) | No |
| Tesseract 5.5.2 | vcpkg (`vcpkg.json` @ `d8fc891`, baseline `1e199d32…`); cadena del binario | Apache-2.0 (no verificado en esta revisión) | Ninguno (módulo `text`) | Sí (estático) | Sí (`Apache-2.0.txt`) |
| libjpeg-turbo 3.1.3 (feature `jpeg7`) | vcpkg; cadena "libjpeg-turbo version 3.1.3" | No verificado en esta revisión | Decodificación JPEG en OpenCV (SCI-PH entrega PNG normalizado) | Sí (estático) | No |
| libpng 1.6.55, zlib 1.3.2, libtiff 4.7.1, libwebp 1.6.0, OpenJPEG 2.5.4 | `getBuildInformation()` + vcpkg | No verificado en esta revisión | Códecs de imagen de OpenCV (SCI-PH usa PNG) | Sí (estático) | No |
| Intel IPP ICV 2026.0.0 (+ IPP IW) | `getBuildInformation()`: "Intel IPP: 2026.0.0", "Intel IPP IW: sources (2026.0.0)" | No verificado en esta revisión (licencia propia de Intel para ICV) | Aceleración interna de OpenCV | Sí (estático) | No |
| Protobuf 3.19.1, libclapack, ittnotify | `getBuildInformation()` (3rdparty internos de OpenCV) | No verificado en esta revisión | Internos de `dnn`/`core` | Sí (estático) | No |
| Otros detectados solo por cadenas del binario: libarchive 3.8.5, liblzma, zstd, bzip2, LERC, libdeflate, FreeType, OpenEXR | escaneo de cadenas de `libOpenCvSharpExtern.so` (no hay inventario upstream) | No verificable desde upstream para este binario | Ninguno | Probablemente (estático) | No |
| YuNet 2023mar | github.com/opencv/opencv_zoo `models/face_detection_yunet/LICENSE` | MIT (Copyright 2020 Shiqi Yu) | Detección de rostro | Sí, si la imagen se construye con modelos (`FACE_MODELS=include`) | Sí (`YuNet-LICENSE.txt`) |
| SFace 2021dec FP32 | github.com/opencv/opencv_zoo `models/face_recognition_sface/LICENSE` | Apache-2.0 | Embedding facial (128 floats) | Igual que YuNet | Sí (`Apache-2.0.txt`) |
| Face Synthetics `dataset_100.zip` (SHA-256 `df6eed9e…ab99`) | facesyntheticspubwedata.z6.web.core.windows.net/iccv-2021/ | Microsoft Research Use of Data Agreement v1.0 (uso de investigación **no comercial**; quien redistribuya debe obligar al receptor a la misma licencia) | Solo pruebas locales opt-in (`SCIPH_FACE_MODELS=1`) | **No**: nunca versionado, ni en Docker, ni en artefactos de CI | N/A |
| Azure.Storage.Blobs 12.29.2 / Azure.Core 1.55.0 | nuget.org | MIT | Almacén de plantillas cifradas | Sí | No (MIT, paquete con su licencia) |

## Hechos sobre FFmpeg relevantes para la redistribución

1. El binario Linux que redistribuiría la imagen enlaza FFmpeg **estáticamente** (README del runtime; `build_static_deps.sh`:
   `--enable-static --disable-shared`, "FFmpeg is LGPL v2.1+, statically linked"; el linker oculta sus símbolos con
   `--exclude-libs`).
2. La "License Compliance Checklist" de https://ffmpeg.org/legal.html recomienda, entre otros puntos, **enlazar
   dinámicamente** (punto 2), **distribuir el código fuente** de FFmpeg que corresponda exactamente al binario (puntos 3–4),
   documentar cómo se compiló (punto 6), alojarlo junto al binario (punto 8) y mencionar FFmpeg/LGPL en el "about box" y en
   el EULA (puntos 10–11). La misma página dice que la lista "is not the only way to comply with the license".
3. La LGPL-2.1, sección 6, condiciona la distribución de una obra que **enlaza estáticamente** la biblioteca a
   proporcionar el código fuente de la biblioteca y los medios para volver a enlazar la obra (6a), o una oferta escrita
   (6c), entre otras opciones.
4. El script de upstream reutiliza una instalación de FFmpeg cacheada si existe ("Reuse an existing FFmpeg installation in
   mounted cache"); las versiones de las bibliotecas del binario coinciden con FFmpeg 7.1.1, pero el artefacto de CI con la
   configuración exacta (`ffmpeg-feature-inventory`) no se ha descargado ni verificado.
5. SCI-PH **no usa** `videoio` ni FFmpeg; viene incluido en el runtime oficial. El perfil `slim` no lleva FFmpeg pero
   tampoco `dnn`, que el proveedor necesita (D0/D1: no cambiar de runtime sin autorización).

## Puntos abiertos

Ninguno puede cerrarse con hechos de upstream; requieren decisión del Tech Lead o asesoría legal:

1. **FFmpeg LGPL-2.1 enlazado estáticamente en `libOpenCvSharpExtern.so`.** Qué debe acompañar a la imagen Docker de
   SCI-PH si se distribuye (fuente de FFmpeg 7.1.1, fuente y scripts de OpenCvSharp/OpenCV en `d8fc891` para volver a
   enlazar, oferta escrita, avisos en la UI/EULA) es una interpretación legal que este documento no hace.
2. **Inventario completo de componentes estáticos.** Upstream no publica un THIRD-PARTY-NOTICES del binario; parte del
   inventario de arriba (libarchive y sus dependencias de compresión, FreeType, OpenEXR, etc.) solo se infiere de cadenas
   del binario y no se ha verificado contra el árbol de vcpkg del baseline `1e199d32…`.
3. **Avisos sin texto incluido** (libjpeg-turbo, libpng, zlib, libtiff, libwebp, OpenJPEG, Intel IPP ICV, protobuf,
   LAPACK, ittnotify, Leptonica si está): no se han copiado sus textos de licencia a la imagen porque no se pudo fijar desde
   upstream la lista exacta y las versiones.
4. **Face Synthetics:** si SCI-PH se comercializa, la muestra de pruebas debe reemplazarse por una fuente con licencia
   compatible.

Opciones que requieren autorización explícita (no aplicadas): asesoría legal sobre la distribución tal cual; construir un
runtime propio con FFmpeg dinámico o sin `videoio`; o restringir la imagen con OpenCV a uso interno no distribuido.

## Qué contiene la imagen Docker del Web BFF

- `/app/licenses/face-opencv-third-party.md` (este documento) y `/app/licenses/*.txt` (`docs/legal/licenses/`).
- Los modelos solo si se construye con `FACE_MODELS=include` (por defecto), descargados y verificados por SHA-256 en una
  etapa dedicada (`scripts/biometrics/fetch-face-models.sh`).
- **Nunca**: claves de cifrado, cadenas de conexión, plantillas ni la muestra Face Synthetics.
