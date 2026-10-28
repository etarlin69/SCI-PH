# Componentes de terceros del proveedor facial local (OpenCV)

- **Ámbito:** proveedor `SCIPH-FaceOpenCV` ([ADR-012](../adr/ADR-012-face-verification.md), PR 2/2): API administrada
  OpenCvSharp5, runtime nativo, modelos ONNX y la muestra de prueba Face Synthetics.
- **Fecha de la revisión:** 2026-10-28.
- **Naturaleza:** inventario de **hechos verificables** (commits fijados, configuración CMake generada, archivos de licencia
  que instala el propio build, dependencias ELF, metadatos de paquetes). **No es una opinión legal.**

## Decisión de runtime (Tech Lead)

| Uso | Runtime nativo | Se distribuye |
|---|---|---|
| **Producción Linux x64** (imagen Docker del Web BFF) | **Build propio desde source** (`scripts/biometrics/build-opencv-linux.sh`, etapa `opencv-native` del Dockerfile): OpenCV + OpenCvSharpExtern mínimos, sin FFmpeg, Tesseract, GStreamer, V4L ni GTK/X11 | Sí |
| Desarrollo y tests en Windows | `OpenCvSharp5.runtime.win` 5.0.0.20261003 (NuGet; incluye FFmpeg y Tesseract) | **No**. Si algún día se distribuye un artefacto Windows, necesita su propio gate de licencias |
| Tests en runners Linux de CI | `OpenCvSharp5.official.runtime.linux-x64.headless` 5.0.0.20261003, referenciado **solo** por `SCI-PH.IntegrationTests` (`PrivateAssets="all"`) | **No**: nunca entra en el publish del Web BFF ni en la imagen |

El paquete oficial Linux **no** es runtime de producción: SCI-PH no necesita FFmpeg ni Tesseract, y la distribución no
depende de interpretar las obligaciones de componentes estáticos que no se usan.

## Fuentes fijadas del runtime Linux

| Fuente | Commit exacto | Verificación |
|---|---|---|
| OpenCvSharp (`OpenCvSharpExtern`) | `d8fc8914e09b43fe6e2ec71c1e376aef5188acf1` | `<repository commit>` de los `.nuspec` de OpenCvSharp5 5.0.0.20261003 (misma versión que la API administrada) |
| OpenCV | `40738fb16ceddb5fb3fea747585f7ce6abb0605b` (tag `5.0.0`) | submódulo `opencv` de ese commit de OpenCvSharp |
| OpenCV contrib | `755e50675d97db9b7d449d8bd6b09888646f6c6e` | submódulo `opencv_contrib` de ese commit de OpenCvSharp |
| Imagen de compilación | `debian:bookworm@sha256:d74df7598108a173a621eda4d58a2adbf850b2d9a9d81bc2cca1a6c21174a880` (glibc 2.36, GCC 12.2, CMake 3.25) | misma glibc que `mcr.microsoft.com/dotnet/aspnet:9.0` (Debian 12) |

El script hace `git fetch` de cada commit, comprueba que `HEAD` sea exactamente ese commit y falla si no lo es. Nunca
usa una rama ni "latest". Los binarios no se versionan en git.

## Configuración real (generada por CMake)

- **Módulos pedidos** (`BUILD_LIST`): `core, imgproc, imgcodecs, dnn, objdetect`, que son los que usa el proveedor, más
  `xobjdetect, xfeatures2d, ximgproc, xphoto, bgsegm, img_hash`. Estos últimos los incluye **sin condición**
  `src/OpenCvSharpExtern/include_opencv.h` en el commit fijado: sin ellos OpenCvSharpExtern no compila. Son módulos de
  `opencv_contrib` (Apache-2.0) sin dependencias externas, y entran por `OPENCV_EXTRA_MODULES_PATH` **solo** esos seis
  directorios. Es el mismo criterio que el perfil `slim` oficial de OpenCvSharp (`cmake/opencv_build_options_slim.cmake`),
  pero con `dnn` y sin los módulos opcionales.
- **Construidos** (resumen de CMake, "To be built"): `bgsegm core dnn features flann geometry img_hash imgcodecs imgproc
  objdetect photo stereo video xfeatures2d ximgproc xobjdetect xphoto`. CMake añade `features, flann, geometry, photo,
  stereo, video` como dependencias de los anteriores. **Distribuidos:** las 16 librerías que `libOpenCvSharpExtern.so` y
  sus dependencias piden (`NEEDED`); todos menos `xfeatures2d`.
- **No construidos:** `highgui, videoio` (desactivados), `calib, ptcloud, stitching` (desactivados por dependencia),
  `text, ml` y el resto de contrib (no pedidos).
- **OpenCvSharpExtern:** `NO_CONTRIB NO_VIDEOIO NO_HIGHGUI NO_STITCHING NO_CALIB NO_PTCLOUD NO_ML` (los `NO_*` de los
  módulos ausentes se calculan a partir de lo instalado). Tesseract solo se enlaza si `find_package(Tesseract)` lo
  encuentra, y en la imagen de compilación no está instalado.
- **Desactivado explícitamente:** FFmpeg, GStreamer, V4L, 1394, Orbbec, plugins de videoio/highgui, GTK, Qt, OpenGL, VTK,
  Wayland, JPEG, TIFF, WebP, OpenJPEG, JasPer, OpenEXR, AVIF, JPEG-XL, GDAL, GDCM, códecs HDR/SUNRASTER/PXM/PFM/GIF,
  flatbuffers, OpenVINO, Vulkan, CUDA, CANN, TIM-VX, WebNN, OpenCL, Intel IPP/ITT, VA, LAPACK, Eigen, TBB, OpenMP,
  carotene, KleidiCV, ADE, quirc, HDF5, fuente Unicode descargable (`WITH_UNIFONT=OFF`) y datos VGG/BoostDesc
  (`OPENCV_SKIP_FEATURES2D_DOWNLOADING=ON`).
- **Descargas en configure:** ninguna (`CMakeDownloadLog.txt` vacío; el gate lo exige).

El set completo de opciones, la lista de módulos, `cv::getBuildInformation()`, las dependencias ELF y el SHA-256 de cada
librería quedan en `/app/licenses/opencv-runtime-manifest.txt` de la imagen, que genera el propio script.

## Gate de exclusión (lo aplica el script; el build falla si algo aparece)

| Verificación | FFmpeg | Tesseract | GStreamer | GTK/X11 | videoio / highgui | text / stitching |
|---|---|---|---|---|---|---|
| `CMakeCache.txt` (`WITH_*` / `BUILD_opencv_*` = OFF) | OFF | (no se busca) | OFF | OFF (`WITH_GTK`, `WITH_QT`) | OFF | no pedidos |
| Módulos construidos e instalados | — | — | — | — | no | no |
| `readelf -d` (NEEDED) de cada `.so` | no | no | no | no | — | — |
| `strings` de cada `.so` (código estático embebido) | no | no | no | no | — | — |
| `cv::getBuildInformation()` | no | no | no | no | — | — |

Dependencias dinámicas de todo el runtime (`readelf`): solo librerías de OpenCV del propio build (`RPATH $ORIGIN`) y del
sistema base de Debian 12 (`libstdc++.so.6`, `libgcc_s.so.1`, `libm.so.6`, `libc.so.6`, `ld-linux-x86-64.so.2`).

## Inventario de lo que se redistribuye

Licencia "según upstream" = el archivo de licencia que acompaña al componente en el commit fijado. Todos los textos
viajan en la imagen: `/app/licenses/opencv-runtime/` (copiados por el build) y `/app/licenses/` (este documento y
`docs/legal/licenses/`).

| Componente | Versión / commit | Licencia (según upstream) | Fuente | Vínculo | Por qué está |
|---|---|---|---|---|---|
| OpenCvSharp (API administrada `OpenCvSharp.dll`) | 5.0.0.20261003 | Apache-2.0 | nuget.org `OpenCvSharp5` (repo `d8fc891`) | ensamblado .NET | API del proveedor |
| OpenCvSharpExtern (`libOpenCvSharpExtern.so`) | `d8fc891` | Apache-2.0 (`OpenCvSharp-LICENSE`) | github.com/shimat/opencvsharp | dinámico (la carga .NET) | puente nativo de la API |
| OpenCV (16 librerías `libopencv_*.so.500`) | 5.0.0 `40738fb` | Apache-2.0 (`OpenCV-LICENSE`) | github.com/opencv/opencv | dinámico | dnn, objdetect (`FaceDetectorYN`, `FaceRecognizerSF`), imgcodecs, imgproc, core y sus dependencias |
| OpenCV contrib (xobjdetect, ximgproc, xphoto, bgsegm, img_hash) | `755e506` | Apache-2.0 (`OpenCV-contrib-LICENSE`) | github.com/opencv/opencv_contrib | dinámico | requeridos por `include_opencv.h` |
| zlib | 1.3.2 | zlib (`zlib-LICENSE`) | `opencv/3rdparty/zlib` | estático (en libopencv_core / imgcodecs) | compresión PNG |
| libpng | 1.6.57 | PNG Reference Library License v2 (`libpng-LICENSE`) | `opencv/3rdparty/libpng` | estático (libopencv_imgcodecs) | decodificar la captura normalizada (PNG) |
| Protocol Buffers | 3.19.1 | BSD-3-Clause (`protobuf-LICENSE`) | `opencv/3rdparty/protobuf` | estático (libopencv_dnn) | leer modelos ONNX |
| MLAS (Microsoft) | onnxruntime `62f742f` (subset SGEMM) | MIT (`mlas-LICENSE`) | `opencv/3rdparty/mlas` | estático (libopencv_dnn) | GEMM de dnn |
| dlpack | encabezados | Apache-2.0 (`dlpack-LICENSE`) | `opencv/3rdparty/dlpack` | solo encabezados (dnn) | tipos de tensores de dnn |
| Berkeley SoftFloat | Release 3c | BSD-3-Clause (`SoftFloat-COPYING.txt`) | `opencv/modules/core/3rdparty/SoftFloat` | estático (libopencv_core) | aritmética de coma flotante portable de core |
| Annoy (Spotify) | encabezado `annoylib` | Apache-2.0 (`annoylib-LICENSE`) | `opencv/modules/features/3rdparty/annoy` | estático (libopencv_features) | módulo features (dependencia de objdetect) |
| Fuente Rubik | `Rubik.ttf`, `Rubik-Italic.ttf` | SIL OFL 1.1 (`fonts-Rubik_OFL.txt`) | `opencv/modules/imgproc/fonts` | embebida (libopencv_imgproc, siempre) | fuente integrada de `putText` (no usada) |
| chi_table (MSCR) | — | BSD (Per-Erik Forssen, `mscr-chi_table_LICENSE.txt`) | `opencv/modules/features/3rdparty/mscr` | estático (libopencv_features) | módulo features |
| YuNet `face_detection_yunet_2023mar.onnx` | SHA-256 `8f2383e4…52fa4` | MIT (Shiqi Yu, `YuNet-LICENSE.txt`) | github.com/opencv/opencv_zoo | archivo de datos | detección de rostro |
| SFace `face_recognition_sface_2021dec.onnx` | SHA-256 `0ba9fbfa…e79` | Apache-2.0 (`Apache-2.0.txt`) | github.com/opencv/opencv_zoo | archivo de datos | embedding facial |
| Azure.Storage.Blobs 12.29.2 / Azure.Core 1.55.0 | — | MIT (licencia del paquete) | nuget.org | ensamblado .NET | almacén de plantillas cifradas |

La lista de componentes estáticos es la que **OpenCV declara** con `ocv_install_3rdparty_licenses` para los módulos
construidos (directorio `licenses/` del install), más MLAS, que está compilado (`DNN MLAS: YES`) y cuya licencia el
script añade porque OpenCV no la instala. `xfeatures2d` se compila porque OpenCvSharpExtern incluye sus encabezados, pero
el extern no la enlaza (`--as-needed`) y su librería **no se copia**: KAZE/AKAZE no se redistribuyen (sus textos aparecen
igualmente en `licenses/` porque los instala OpenCV). Componentes estáticos no identificados: **ninguno**.

## Fuera de la distribución

- **Face Synthetics** (`dataset_100.zip`, SHA-256 `df6eed9e…ab99`): Microsoft Research Use of Data Agreement v1.0 (uso de
  investigación no comercial). Solo para pruebas locales opt-in (`SCIPH_FACE_MODELS=1`), descargado con
  `scripts/biometrics/fetch-face-fixtures`. Nunca versionado, ni en Docker ni en artefactos de CI; hay que reemplazarlo si
  SCI-PH se comercializa.
- **Runtimes NuGet de OpenCvSharp** (Windows y Linux oficial): solo para desarrollo y tests; no entran en la imagen. El
  paquete Windows incluye FFmpeg (LGPL-2.1, `opencv_videoio_ffmpeg500_64.dll`) y Tesseract; si alguna vez se distribuye
  un artefacto Windows, necesita su propio gate.
