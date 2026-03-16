// API_URL is replaced by sed in the GitHub Actions deploy-ui.yml workflow.
// Locally override via: ng serve --configuration production
export const environment = {
  production: true,
  apiBaseUrl: '',   // replaced at CI time: sed patches this to your Railway/Render URL
};
