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
function getAsset(name) {
  const assets = stats.assetsByChunkName[name];
  if (Array.isArray(assets)) {
    return assets[0];
  } else {
    return assets;
  }
}

for (const assetName of assetOrder) {
  const fileName = getAsset(assetName);
  delete stats.assetsByChunkName[assetName];
  addAsset(assetName, fileName);
}
for (const asset of Object.keys(stats.assetsByChunkName)) {
  const fileName = getAsset(asset);
  addAsset(asset, fileName);
}
fs.writeFileSync(assetsJson, JSON.stringify(assets, undefined, 2));

