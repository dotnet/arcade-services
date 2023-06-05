const proxy = {
  "target": "http://localhost:8080",
  "secure": false,
  "logLevel": "debug",
};

module.exports = {
  "/_/*": proxy,
  "/libs/*": proxy,
  "/favicon.ico": proxy,
};
