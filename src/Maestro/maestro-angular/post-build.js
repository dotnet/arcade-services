const path = require("path");
const fs = require("fs");

const outDir = path.join(__dirname, "dist/maestro-angular");
const statsJson = path.join(outDir, "stats.json");
const assetsJson = path.join(outDir, "assets.json");

console.log(`READ ${statsJson}`);
const stats = JSON.parse(fs.readFileSync(statsJson));


const assetOrder = [
  "runtime",
  "es2015-polyfills",
  "polyfills",
  "vendor",
  "main",
];

console.log(`WRITE ${assetsJson}`);
const assets = {
  "scripts": [],
  "styles": [],
};
function addAsset(assetName, fileName) {
  if (fileName.endsWith(".js")) {
    assets.scripts.push({
      name: assetName,
      file: fileName,
    });
  } else {
    assets.styles.push({
      name: assetName,
      file: fileName,
    });
  }
}
for (const assetName of assetOrder) {
  const fileName = stats.assetsByChunkName[assetName];
  delete stats.assetsByChunkName[assetName];
  addAsset(assetName, fileName);
}
for (const asset of Object.keys(stats.assetsByChunkName)) {
  const fileName = stats.assetsByChunkName[asset];
  addAsset(asset, fileName);
}
fs.writeFileSync(assetsJson, JSON.stringify(assets, undefined, 2));

