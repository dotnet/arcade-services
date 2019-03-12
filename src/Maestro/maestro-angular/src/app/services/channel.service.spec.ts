import { TestBed } from "@angular/core/testing";
import { of } from 'rxjs';

import { ChannelService } from "./channel.service";
import { MaestroService } from 'src/maestro-client';

describe("ChannelService", () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [
        {
          provide: MaestroService,
          useValue: {
            channels: {
              listChannelsAsync() {
                return of();
              },
            },
          },
        },
    ],
  }));

  it("should be created", () => {
    const service: ChannelService = TestBed.get(ChannelService);
    expect(service).toBeTruthy();
  });
});
