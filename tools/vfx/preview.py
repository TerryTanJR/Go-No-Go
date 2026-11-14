# 把透明 PNG 疊在深藍天空色底上並排，方便檢查
import sys
from PIL import Image
paths = sys.argv[2:]
ims = [Image.open(p).convert("RGBA") for p in paths]
w, h = ims[0].size
out = Image.new("RGBA", (w * len(ims), h), (18, 28, 48, 255))
for i, im in enumerate(ims):
    out.alpha_composite(im, (i * w, 0))
out.convert("RGB").save(sys.argv[1])
print("alpha max per frame:", [im.getchannel("A").getextrema()[1] for im in ims])
