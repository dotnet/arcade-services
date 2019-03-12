import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { RecentBuildComponent } from './recent-build.component';

describe('RecentBuildComponent', () => {
  let component: RecentBuildComponent;
  let fixture: ComponentFixture<RecentBuildComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ RecentBuildComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(RecentBuildComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
