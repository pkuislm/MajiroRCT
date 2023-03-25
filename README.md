# MajiroRCImage

转换Majiro的rct和rc8的工具  

可以将rct + rc8转换为32位png，也可以只将rct/rc8转换为24位png，提供密码时支持图片加密/解密 

根据png本身的通道数，自动生成rct或rct + rc8 

rct和rc8均支持回封时压缩 

目前rct只支持标记为`TC00/TS00`的，`TC01`还没动工（有点不想写了）  

#### 命令行解释： 
`-k`：设置密码。参数为需要设置的密码。当rct本身未被解密时（标记为`TS00`），或是需要在回封时加密时使用。

`-c`：表示在回封时加密图片，没有参数。不添加该项时默认不加密。**此项必须与`-k`连用**

`-e`：从RCT/RC8提取到PNG。参数为RCT/RC8文件数组。（请注意，假设rct文件为`image.rct`，若其所在目录下存在 `image_.rc8`时，`image_.rc8`会被自动读取并作为alpha mask合并到输出文件中。此时输出文件即为带透明通道的png。） 

`-p`：从PNG生成RCT。参数为PNG文件数组。（若提供的PNG为32位，则会同时生成rct和rc8文件。否则只生成rct文件） 

#### 范例
+ 解压图片（无密码）
  + `MajiroRCImage.exe -e "E:\Game\ext\image.rct" "E:\Game\ext\image2.rct"`
+ 解压图片（有密码）
  + `MajiroRCImage.exe -k "chuable" -e "E:\Game\ext\image.rct" "E:\Game\ext\image2.rct"`
+ 回封图片（无密码）
  + `MajiroRCImage.exe -p "E:\Game\ext\image.png" "E:\Game\ext\image2.png"`
+ 回封图片（有密码）
  + `MajiroRCImage.exe -k "chuable" -c -p "E:\Game\ext\image.png" "E:\Game\ext\image2.png"`