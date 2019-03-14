import { MaestroOptions } from 'src/maestro-client';

export const environment = {
  production: true,
};

export const maestroOptions: Partial<MaestroOptions> = {
  baseUrl: "/_/",
}
